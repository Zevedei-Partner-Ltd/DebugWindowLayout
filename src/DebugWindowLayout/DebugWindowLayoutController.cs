using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace DebugWindowLayout
{
    internal sealed class DebugWindowLayoutController
    {
        private readonly AsyncPackage _package;
        private readonly DTE2 _dte;
        private bool _arrangeLoopRunning;
        private bool _arrangeAgain;

        public DebugWindowLayoutController(AsyncPackage package, DTE2 dte)
        {
            _package = package;
            _dte = dte;
        }

        public void ScheduleAutoArrange()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            StartArrangeLoop(requireAutoEnabled: true, bringToFront: false);
        }

        public void ArrangeNow()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            StartArrangeLoop(requireAutoEnabled: false, bringToFront: true);
        }

        public void OpenConfig()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _package.JoinableTaskFactory.RunAsync(async delegate
            {
                try
                {
                    var configPath = await GetConfigPathAsync();
                    if (configPath == null)
                    {
                        await ShowMessageAsync("Open a solution first.");
                        return;
                    }

                    if (!File.Exists(configPath))
                    {
                        var processes = await GetDebuggedProcessesAsync();
                        var config = CreateStarterConfig(processes);
                        config.Save(configPath);
                    }

                    await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
                    VsShellUtilities.OpenDocument(_package, configPath);
                }
                catch (Exception ex)
                {
                    await ShowMessageAsync("Could not open the layout config:\n" + ex.Message);
                }
            }).FileAndForget("DebugWindowLayout/OpenConfig");
        }

        /// <summary>
        /// Adds rules for all currently debugged program windows to the existing config
        /// without touching rules that are already present.
        /// </summary>
        public void CaptureWindows()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _package.JoinableTaskFactory.RunAsync(async delegate
            {
                try
                {
                    await CaptureWindowsAsync();
                }
                catch (Exception ex)
                {
                    await ShowMessageAsync("Could not capture debug windows:\n" + ex.Message);
                }
            }).FileAndForget("DebugWindowLayout/CaptureWindows");
        }

        private void StartArrangeLoop(bool requireAutoEnabled, bool bringToFront)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_arrangeLoopRunning)
            {
                _arrangeAgain = true;
                return;
            }

            _arrangeLoopRunning = true;
            _package.JoinableTaskFactory.RunAsync(async delegate
            {
                try
                {
                    do
                    {
                        _arrangeAgain = false;
                        await ArrangeWithRetriesAsync(requireAutoEnabled, bringToFront);
                    }
                    while (_arrangeAgain);
                }
                catch (Exception ex)
                {
                    await ShowMessageAsync("Debug Window Layout failed:\n" + ex.Message);
                }
                finally
                {
                    _arrangeLoopRunning = false;
                }
            }).FileAndForget("DebugWindowLayout/ArrangeLoop");
        }

        private async Task ArrangeWithRetriesAsync(bool requireAutoEnabled, bool bringToFront)
        {
            var configPath = await GetConfigPathAsync();
            var config = LayoutConfig.LoadOrDefault(configPath);

            if (!config.Enabled || (requireAutoEnabled && !config.AutoArrangeOnDebug))
                return;

            var stableIterations = 0;
            var lastFoundCount = -1;

            for (var attempt = 0; attempt < config.RetryCount; attempt++)
            {
                var processes = await GetDebuggedProcessesAsync();
                if (processes.Count > 0)
                {
                    var foundCount = ArrangeOnce(processes, config, bringToFront);

                    if (foundCount > 0 && foundCount == lastFoundCount)
                        stableIterations++;
                    else
                        stableIterations = 0;

                    lastFoundCount = foundCount;

                    var expectedCount = config.Rules != null && config.Rules.Count > 0
                        ? config.Rules.Count
                        : processes.Count;

                    // Only stop early when every expected debug window has appeared and the set is stable.
                    if (foundCount >= expectedCount && stableIterations >= 2)
                        break;
                }

                await Task.Delay(config.RetryMilliseconds);
            }
        }

        private int ArrangeOnce(IReadOnlyList<DebugProcessInfo> processes, LayoutConfig config, bool bringToFront)
        {
            var monitors = MonitorManager.GetMonitors();
            if (monitors.Count == 0)
                return 0;

            var windows = WindowManager.EnumerateVisibleWindows();
            if (config.Rules != null && config.Rules.Count > 0)
                return ArrangeRules(processes, windows, monitors, config, bringToFront);

            return ArrangeAutoGrid(processes, windows, monitors, config, bringToFront);
        }

        private int ArrangeRules(
            IReadOnlyList<DebugProcessInfo> processes,
            IReadOnlyList<WindowInfo> windows,
            IReadOnlyList<MonitorInfo> monitors,
            LayoutConfig config,
            bool bringToFront)
        {
            var assigned = new HashSet<IntPtr>();
            var moved = 0;

            foreach (var rule in config.Rules)
            {
                var window = WindowManager.FindForRule(rule, processes, windows, assigned);
                if (window == null)
                    continue;

                var monitor = MonitorManager.SelectMonitor(monitors, rule.Monitor ?? config.TargetMonitor);
                var area = config.UseWorkingArea ? monitor.WorkingArea : monitor.Bounds;
                var normalized = rule.Bounds?.Clamp() ?? ZoneToBounds(rule.Zone);
                var target = ToPixelBounds(area, normalized, config.Margin);

                if (WindowManager.MoveWindow(window, target, config.RestoreBeforeMove, bringToFront))
                {
                    assigned.Add(window.Handle);
                    moved++;
                }
            }

            return moved;
        }

        private int ArrangeAutoGrid(
            IReadOnlyList<DebugProcessInfo> processes,
            IReadOnlyList<WindowInfo> windows,
            IReadOnlyList<MonitorInfo> monitors,
            LayoutConfig config,
            bool bringToFront)
        {
            var debugWindows = WindowManager.MatchWindowsToProcesses(processes, windows)
                .Select(m => m.Window)
                .ToList();
            if (debugWindows.Count == 0)
                return 0;

            var monitor = MonitorManager.SelectMonitor(monitors, config.TargetMonitor);
            var area = config.UseWorkingArea ? monitor.WorkingArea : monitor.Bounds;
            var cells = BuildGrid(debugWindows.Count);

            for (var i = 0; i < debugWindows.Count; i++)
            {
                var target = ToPixelBounds(area, cells[i], config.Margin);
                WindowManager.MoveWindow(debugWindows[i], target, config.RestoreBeforeMove, bringToFront);
            }

            return debugWindows.Count;
        }

        private async Task<IReadOnlyList<DebugProcessInfo>> GetDebuggedProcessesAsync()
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
            var result = new List<DebugProcessInfo>();

            try
            {
                foreach (EnvDTE.Process process in _dte.Debugger.DebuggedProcesses)
                {
                    var name = WindowManager.NormalizeProcessName(process.Name);
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        try { name = System.Diagnostics.Process.GetProcessById(process.ProcessID).ProcessName; }
                        catch { }
                    }

                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        result.Add(new DebugProcessInfo
                        {
                            ProcessId = process.ProcessID,
                            Name = name
                        });
                    }
                }
            }
            catch
            {
                // Debugger state can change while enumerating. The retry loop will try again.
            }

            return result
                .GroupBy(p => p.ProcessId)
                .Select(g => g.First())
                .ToList();
        }

        private async Task<string> GetConfigPathAsync()
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
            return LayoutConfigStorage.GetConfigPath(_dte);
        }

        private LayoutConfig CreateStarterConfig(IReadOnlyList<DebugProcessInfo> processes)
        {
            var config = new LayoutConfig();
            var unique = processes.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            if (unique.Count == 1)
            {
                config.Rules.Add(new LayoutRule { Process = unique[0], Zone = LayoutZone.Full });
            }
            else if (unique.Count == 2)
            {
                config.Rules.Add(new LayoutRule { Process = unique[0], Zone = LayoutZone.Left });
                config.Rules.Add(new LayoutRule { Process = unique[1], Zone = LayoutZone.Right });
            }
            else if (unique.Count > 0)
            {
                var grid = BuildGrid(unique.Count);
                for (var i = 0; i < unique.Count; i++)
                {
                    config.Rules.Add(new LayoutRule
                    {
                        Process = unique[i],
                        Bounds = grid[i]
                    });
                }
            }

            return config;
        }

        private async Task CaptureWindowsAsync()
        {
            var configPath = await GetConfigPathAsync();
            if (configPath == null)
            {
                await ShowMessageAsync("Open a solution first.");
                return;
            }

            var processes = await GetDebuggedProcessesAsync();
            if (processes.Count == 0)
            {
                await ShowMessageAsync("No debugged processes found. Start debugging first, then capture again.");
                return;
            }

            await _package.JoinableTaskFactory.SwitchToMainThreadAsync();

            var monitors = MonitorManager.GetMonitors();
            if (monitors.Count == 0)
            {
                await ShowMessageAsync("No monitors could be enumerated.");
                return;
            }

            var windows = WindowManager.EnumerateVisibleWindows();
            var config = LayoutConfig.LoadOrDefault(configPath);

            // Windows already matched by existing rules keep their rule and are never duplicated.
            var covered = new HashSet<IntPtr>();
            foreach (var rule in config.Rules)
            {
                var matched = WindowManager.FindForRule(rule, processes, windows, covered);
                if (matched != null)
                    covered.Add(matched.Handle);
            }

            var matches = WindowManager.MatchWindowsToProcesses(processes, windows, covered);

            var added = new List<string>();
            foreach (var match in matches)
            {
                var rule = BuildRuleFromWindow(match, config, monitors);
                if (rule != null)
                {
                    config.Rules.Add(rule);
                    added.Add(match.Process.Name);
                }
            }

            if (added.Count == 0)
            {
                await ShowMessageAsync(config.Rules.Count > 0
                    ? "Every debugged window is already covered by the existing rules. Nothing was changed."
                    : "No visible windows of the debugged processes were found.");
                return;
            }

            config.Save(configPath);
            RefreshOptionsPage();
            await ShowMessageAsync(
                "Added " + added.Count + " rule(s): " + string.Join(", ", added.Distinct(StringComparer.OrdinalIgnoreCase)) + ".\n" +
                "Existing rules were kept unchanged.");
        }

        /// <summary>
        /// Reloads the cached options page instance (if any) so it immediately shows the captured rules.
        /// Must be called on the UI thread.
        /// </summary>
        private void RefreshOptionsPage()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_package.GetDialogPage(typeof(DebugWindowLayoutOptionsPage)) is DebugWindowLayoutOptionsPage page)
                page.LoadSettingsFromStorage();
        }

        private static LayoutRule BuildRuleFromWindow(
            ProcessWindowMatch match,
            LayoutConfig config,
            IReadOnlyList<MonitorInfo> monitors)
        {
            var window = match.Window;
            if (!NativeMethods.GetWindowRect(window.Handle, out var nativeRect))
                return null;

            var rect = Rect.FromNative(nativeRect);
            var monitor = MonitorManager.GetMonitorForWindow(monitors, window.Handle)
                ?? MonitorManager.SelectMonitor(monitors, config.TargetMonitor);
            if (monitor == null)
                return null;

            var area = config.UseWorkingArea ? monitor.WorkingArea : monitor.Bounds;
            if (area.Width <= 0 || area.Height <= 0)
                return null;

            // Compensate the configured margin so re-applying the rule reproduces the captured rectangle.
            var bounds = new NormalizedBounds
            {
                X = (double)(rect.Left - area.Left - config.Margin) / area.Width,
                Y = (double)(rect.Top - area.Top - config.Margin) / area.Height,
                Width = (double)(rect.Width + 2 * config.Margin) / area.Width,
                Height = (double)(rect.Height + 2 * config.Margin) / area.Height
            }.Clamp();

            var rule = new LayoutRule
            {
                Process = match.Process.Name,
                Bounds = bounds
            };

            // Only pin the monitor explicitly when it differs from the global target monitor.
            if (monitor.DisplayNumber.HasValue && monitor.DisplayNumber.Value != config.TargetMonitor)
                rule.Monitor = monitor.DisplayNumber.Value;

            return rule;
        }

        private static List<NormalizedBounds> BuildGrid(int count)
        {
            var result = new List<NormalizedBounds>();
            if (count <= 0)
                return result;

            var columns = (int)Math.Ceiling(Math.Sqrt(count));
            var rows = (int)Math.Ceiling((double)count / columns);
            var cellWidth = 1.0 / columns;
            var cellHeight = 1.0 / rows;

            for (var i = 0; i < count; i++)
            {
                var column = i % columns;
                var row = i / columns;
                result.Add(new NormalizedBounds
                {
                    X = column * cellWidth,
                    Y = row * cellHeight,
                    Width = cellWidth,
                    Height = cellHeight
                });
            }

            return result;
        }

        private static NormalizedBounds ZoneToBounds(LayoutZone zone)
        {
            switch (zone)
            {
                case LayoutZone.Left: return B(0, 0, .5, 1);
                case LayoutZone.Right: return B(.5, 0, .5, 1);
                case LayoutZone.Top: return B(0, 0, 1, .5);
                case LayoutZone.Bottom: return B(0, .5, 1, .5);
                case LayoutZone.TopLeft: return B(0, 0, .5, .5);
                case LayoutZone.TopRight: return B(.5, 0, .5, .5);
                case LayoutZone.BottomLeft: return B(0, .5, .5, .5);
                case LayoutZone.BottomRight: return B(.5, .5, .5, .5);
                default: return B(0, 0, 1, 1);
            }
        }

        private static NormalizedBounds B(double x, double y, double width, double height) =>
            new NormalizedBounds { X = x, Y = y, Width = width, Height = height };

        private static Rect ToPixelBounds(Rect area, NormalizedBounds normalized, int margin)
        {
            normalized = normalized.Clamp();
            var left = area.Left + (int)Math.Round(area.Width * normalized.X) + margin;
            var top = area.Top + (int)Math.Round(area.Height * normalized.Y) + margin;
            var right = area.Left + (int)Math.Round(area.Width * (normalized.X + normalized.Width)) - margin;
            var bottom = area.Top + (int)Math.Round(area.Height * (normalized.Y + normalized.Height)) - margin;

            return new Rect
            {
                Left = left,
                Top = top,
                Right = Math.Max(left + 100, right),
                Bottom = Math.Max(top + 80, bottom)
            };
        }

        private async Task ShowMessageAsync(string message)
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
            VsShellUtilities.ShowMessageBox(
                _package,
                message,
                "Debug Window Layout",
                OLEMSGICON.OLEMSGICON_INFO,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }
}
