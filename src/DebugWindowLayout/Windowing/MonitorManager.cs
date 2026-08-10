using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace DebugWindowLayout
{
    internal static class MonitorManager
    {
        public static IReadOnlyList<MonitorInfo> GetMonitors()
        {
            var monitors = new List<MonitorInfo>();
            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
                (IntPtr hMonitor, IntPtr hdc, ref NativeMethods.RECT rect, IntPtr data) =>
                {
                    var info = new NativeMethods.MONITORINFOEX();
                    info.cbSize = Marshal.SizeOf(info);
                    if (NativeMethods.GetMonitorInfo(hMonitor, ref info))
                    {
                        monitors.Add(new MonitorInfo
                        {
                            Handle = hMonitor,
                            DeviceName = info.szDevice,
                            Bounds = Rect.FromNative(info.rcMonitor),
                            WorkingArea = Rect.FromNative(info.rcWork),
                            IsPrimary = (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0,
                            DisplayNumber = ParseDisplayNumber(info.szDevice)
                        });
                    }
                    return true;
                }, IntPtr.Zero);

            return monitors
                .OrderBy(m => m.DisplayNumber ?? int.MaxValue)
                .ThenByDescending(m => m.IsPrimary)
                .ThenBy(m => m.Bounds.Left)
                .ThenBy(m => m.Bounds.Top)
                .ToList();
        }

        public static MonitorInfo SelectMonitor(IReadOnlyList<MonitorInfo> monitors, int requestedNumber)
        {
            if (monitors == null || monitors.Count == 0)
                return null;

            var exact = monitors.FirstOrDefault(m => m.DisplayNumber == requestedNumber);
            if (exact != null)
                return exact;

            var oneBased = requestedNumber - 1;
            if (oneBased >= 0 && oneBased < monitors.Count)
                return monitors[oneBased];

            return monitors.LastOrDefault() ?? monitors[0];
        }

        private static int? ParseDisplayNumber(string deviceName)
        {
            var match = Regex.Match(deviceName ?? string.Empty, @"DISPLAY(?<n>\d+)$", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups["n"].Value, out var n))
                return n;
            return null;
        }
    }

    internal sealed class MonitorInfo
    {
        public IntPtr Handle { get; set; }
        public string DeviceName { get; set; }
        public Rect Bounds { get; set; }
        public Rect WorkingArea { get; set; }
        public bool IsPrimary { get; set; }
        public int? DisplayNumber { get; set; }
    }

    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;

        public static Rect FromNative(NativeMethods.RECT rect) => new Rect
        {
            Left = rect.Left,
            Top = rect.Top,
            Right = rect.Right,
            Bottom = rect.Bottom
        };
    }
}
