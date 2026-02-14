using System.Runtime.InteropServices;

namespace GHelper.Helpers
{
    public class MonitorHelper
    {
        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip,
            MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, DpiType dpiType,
            out uint dpiX, out uint dpiY);

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor,
            ref RECT lprcMonitor, IntPtr dwData);

        private enum DpiType
        {
            Effective = 0,
            Angular = 1,
            Raw = 2,
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        public class MonitorDetails
        {
            public string DeviceName { get; set; }
            public int Left { get; set; }
            public int Top { get; set; }
            public int ScaledWidth { get; set; }
            public int ScaledHeight { get; set; }
            public int PhysicalWidth { get; set; }
            public int PhysicalHeight { get; set; }
            public double ScalingFactor { get; set; }
            public uint DpiX { get; set; }
            public uint DpiY { get; set; }
        }

        public static MonitorDetails GetMonitor(int number)
        {
            List<MonitorDetails> monitors = GetAllMonitors();

            MonitorDetails? monitor = monitors.FirstOrDefault(m => m.DeviceName.Equals($"\\\\.\\DISPLAY{number}"));

            return monitor ?? throw new ArgumentException($"Monitor with device name \\\\.\\DISPLAY{number} not found.",
                    nameof(number));
        }

        public static List<MonitorDetails> GetAllMonitors()
        {
            List<MonitorDetails> monitors = [];

            bool callback(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData)
            {
                MONITORINFOEX mi = new()
                {
                    cbSize = Marshal.SizeOf(typeof(MONITORINFOEX))
                };

                bool success = GetMonitorInfo(hMonitor, ref mi);

                if (success)
                {
                    int physicalWidth = mi.rcMonitor.Right - mi.rcMonitor.Left;
                    int physicalHeight = mi.rcMonitor.Bottom - mi.rcMonitor.Top;

                    // Get DPI for this monitor
                    uint dpiX = 96, dpiY = 96; // Default DPI
                    double scalingFactor = 1.0;

                    try
                    {
                        int result = GetDpiForMonitor(hMonitor, DpiType.Effective, out dpiX, out dpiY);
                        if (result == 0)
                        {
                            scalingFactor = dpiX / 96.0; // 96 DPI is 100% scaling
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.WriteLine($"MonitorHelper exception: {ex.Message}");
                    }

                    int scaledWidth = (int)(physicalWidth / scalingFactor);
                    int scaledHeight = (int)(physicalHeight / scalingFactor);

                    monitors.Add(new MonitorDetails
                    {
                        DeviceName = mi.szDevice,
                        Left = mi.rcMonitor.Left,
                        Top = mi.rcMonitor.Top,
                        PhysicalWidth = physicalWidth,
                        PhysicalHeight = physicalHeight,
                        ScaledWidth = scaledWidth,
                        ScaledHeight = scaledHeight,
                        ScalingFactor = scalingFactor,
                        DpiX = dpiX,
                        DpiY = dpiY
                    });
                }

                return true;
            }

            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);

            return monitors;
        }
    }
}
