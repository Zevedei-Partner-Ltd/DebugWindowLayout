using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace DebugWindowLayout
{
    internal static class WindowManager
    {
        public static IReadOnlyList<WindowInfo> EnumerateVisibleWindows()
        {
            var windows = new List<WindowInfo>();
            var ownPid = (uint)Process.GetCurrentProcess().Id;

            NativeMethods.EnumWindows((hWnd, lParam) =>
            {
                if (!NativeMethods.IsWindowVisible(hWnd))
                    return true;

                NativeMethods.GetWindowThreadProcessId(hWnd, out var pid);
                if (pid == ownPid)
                    return true;

                var titleLength = NativeMethods.GetWindowTextLength(hWnd);
                if (titleLength <= 0)
                    return true;

                var title = new StringBuilder(titleLength + 1);
                NativeMethods.GetWindowText(hWnd, title, title.Capacity);

                var className = new StringBuilder(256);
                NativeMethods.GetClassName(hWnd, className, className.Capacity);

                windows.Add(new WindowInfo
                {
                    Handle = hWnd,
                    ProcessId = unchecked((int)pid),
                    Title = title.ToString(),
                    ClassName = className.ToString()
                });
                return true;
            }, IntPtr.Zero);

            return windows;
        }

        public static WindowInfo FindForRule(
            LayoutRule rule,
            IReadOnlyList<DebugProcessInfo> debugProcesses,
            IReadOnlyList<WindowInfo> windows,
            ISet<IntPtr> alreadyAssigned)
        {
            var candidates = windows.Where(w => !alreadyAssigned.Contains(w.Handle));

            DebugProcessInfo matchingProcess = null;
            if (!string.IsNullOrWhiteSpace(rule.Process))
            {
                matchingProcess = debugProcesses.FirstOrDefault(p =>
                    string.Equals(p.Name, NormalizeProcessName(rule.Process), StringComparison.OrdinalIgnoreCase));

                if (matchingProcess != null)
                {
                    var direct = candidates.FirstOrDefault(w => w.ProcessId == matchingProcess.ProcessId);
                    if (direct != null && TitleMatches(direct.Title, rule.TitleContains))
                        return direct;
                }
            }

            if (!string.IsNullOrWhiteSpace(rule.TitleContains))
            {
                var byTitle = candidates.FirstOrDefault(w =>
                    w.Title?.IndexOf(rule.TitleContains, StringComparison.OrdinalIgnoreCase) >= 0);
                if (byTitle != null)
                    return byTitle;
            }

            // Console windows often belong to conhost/OpenConsole, so their PID differs from the debug target.
            // As a best-effort fallback, match the window title against the target process name.
            if (matchingProcess != null)
            {
                var byProcessTitle = candidates.FirstOrDefault(w =>
                    w.Title?.IndexOf(matchingProcess.Name, StringComparison.OrdinalIgnoreCase) >= 0);
                if (byProcessTitle != null)
                    return byProcessTitle;
            }

            return null;
        }

        public static IReadOnlyList<ProcessWindowMatch> MatchWindowsToProcesses(
            IReadOnlyList<DebugProcessInfo> debugProcesses,
            IReadOnlyList<WindowInfo> windows,
            ISet<IntPtr> excluded = null)
        {
            var found = new List<ProcessWindowMatch>();
            var assigned = new HashSet<IntPtr>();

            foreach (var process in debugProcesses)
            {
                var direct = windows.FirstOrDefault(w =>
                    !assigned.Contains(w.Handle) &&
                    (excluded == null || !excluded.Contains(w.Handle)) &&
                    w.ProcessId == process.ProcessId);

                if (direct == null)
                {
                    direct = windows.FirstOrDefault(w =>
                        !assigned.Contains(w.Handle) &&
                        (excluded == null || !excluded.Contains(w.Handle)) &&
                        w.Title?.IndexOf(process.Name, StringComparison.OrdinalIgnoreCase) >= 0);
                }

                if (direct != null)
                {
                    found.Add(new ProcessWindowMatch { Process = process, Window = direct });
                    assigned.Add(direct.Handle);
                }
            }

            return found;
        }

        /// <summary>
        /// Moves a window to the given bounds and optionally brings it to the foreground.
        /// </summary>
        public static bool MoveWindow(WindowInfo window, Rect bounds, bool restoreBeforeMove, bool bringToFront = false)
        {
            if (window == null || window.Handle == IntPtr.Zero)
                return false;

            if (restoreBeforeMove || (bringToFront && NativeMethods.IsIconic(window.Handle)))
                NativeMethods.ShowWindow(window.Handle, NativeMethods.SW_RESTORE);

            var flags = bringToFront
                ? NativeMethods.SWP_SHOWWINDOW
                : NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW;

            var moved = NativeMethods.SetWindowPos(
                window.Handle,
                bringToFront ? NativeMethods.HWND_TOP : IntPtr.Zero,
                bounds.Left,
                bounds.Top,
                Math.Max(100, bounds.Width),
                Math.Max(80, bounds.Height),
                flags);

            if (moved && bringToFront)
                BringToFront(window.Handle);

            return moved;
        }

        /// <summary>
        /// Activates a window, temporarily attaching to the foreground thread input so the
        /// foreground change is allowed by Windows.
        /// </summary>
        public static void BringToFront(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
                return;

            var foreground = NativeMethods.GetForegroundWindow();
            var foregroundThread = foreground == IntPtr.Zero
                ? 0u
                : NativeMethods.GetWindowThreadProcessId(foreground, out _);
            var currentThread = NativeMethods.GetCurrentThreadId();
            var attached = foregroundThread != 0 && foregroundThread != currentThread
                && NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);

            try
            {
                NativeMethods.BringWindowToTop(handle);
                NativeMethods.SetForegroundWindow(handle);
            }
            finally
            {
                if (attached)
                    NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
            }
        }

        private static bool TitleMatches(string title, string titleContains)
        {
            if (string.IsNullOrWhiteSpace(titleContains))
                return true;

            return title?.IndexOf(titleContains, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static string NormalizeProcessName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            var file = System.IO.Path.GetFileName(name.Trim());
            return file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? file.Substring(0, file.Length - 4)
                : file;
        }
    }

    internal sealed class DebugProcessInfo
    {
        public int ProcessId { get; set; }
        public string Name { get; set; }
    }

    internal sealed class WindowInfo
    {
        public IntPtr Handle { get; set; }
        public int ProcessId { get; set; }
        public string Title { get; set; }
        public string ClassName { get; set; }
    }

    internal sealed class ProcessWindowMatch
    {
        public DebugProcessInfo Process { get; set; }
        public WindowInfo Window { get; set; }
    }
}
