using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
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

        public static IReadOnlyList<WindowInfo> FindForProcesses(
            IReadOnlyList<DebugProcessInfo> debugProcesses,
            IReadOnlyList<WindowInfo> windows)
        {
            var found = new List<WindowInfo>();
            var assigned = new HashSet<IntPtr>();

            foreach (var process in debugProcesses)
            {
                var direct = windows.FirstOrDefault(w =>
                    !assigned.Contains(w.Handle) && w.ProcessId == process.ProcessId);

                if (direct == null)
                {
                    direct = windows.FirstOrDefault(w =>
                        !assigned.Contains(w.Handle) &&
                        w.Title?.IndexOf(process.Name, StringComparison.OrdinalIgnoreCase) >= 0);
                }

                if (direct != null)
                {
                    found.Add(direct);
                    assigned.Add(direct.Handle);
                }
            }

            return found;
        }

        public static bool MoveWindow(WindowInfo window, Rect bounds, bool restoreBeforeMove)
        {
            if (window == null || window.Handle == IntPtr.Zero)
                return false;

            if (restoreBeforeMove)
                NativeMethods.ShowWindow(window.Handle, NativeMethods.SW_RESTORE);

            return NativeMethods.SetWindowPos(
                window.Handle,
                IntPtr.Zero,
                bounds.Left,
                bounds.Top,
                Math.Max(100, bounds.Width),
                Math.Max(80, bounds.Height),
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
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
}
