using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ManualCanDebug
{
    /// <summary>
    /// Keeps a borderless maximized window inside the current monitor's working area.
    /// WindowChrome otherwise allows the window to extend behind the Windows taskbar.
    /// </summary>
    internal sealed class BorderlessWindowSizing : IDisposable
    {
        private const int WmGetMinMaxInfo = 0x0024;
        private const uint MonitorDefaultToNearest = 0x00000002;

        private readonly Window _window;
        private HwndSource _source;

        private BorderlessWindowSizing(Window window)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
            _window.SourceInitialized += Window_SourceInitialized;
            _window.Closed += Window_Closed;
        }

        public static BorderlessWindowSizing Attach(Window window)
        {
            return new BorderlessWindowSizing(window);
        }

        internal static Int32Rect CalculateRelativeWorkArea(Int32Rect monitorArea, Int32Rect workArea)
        {
            return new Int32Rect(
                workArea.X - monitorArea.X,
                workArea.Y - monitorArea.Y,
                Math.Max(0, workArea.Width),
                Math.Max(0, workArea.Height));
        }

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            _source = PresentationSource.FromVisual(_window) as HwndSource;
            _source?.AddHook(WindowProc);
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            Dispose();
        }

        private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message != WmGetMinMaxInfo || lParam == IntPtr.Zero) return IntPtr.Zero;

            IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero) return IntPtr.Zero;

            MonitorInfo monitorInfo = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            if (!GetMonitorInfo(monitor, ref monitorInfo)) return IntPtr.Zero;

            MinMaxInfo minMaxInfo = (MinMaxInfo)Marshal.PtrToStructure(lParam, typeof(MinMaxInfo));
            Int32Rect monitorArea = ToInt32Rect(monitorInfo.Monitor);
            Int32Rect relativeWorkArea = CalculateRelativeWorkArea(monitorArea, ToInt32Rect(monitorInfo.Work));
            minMaxInfo.MaxPosition.X = relativeWorkArea.X;
            minMaxInfo.MaxPosition.Y = relativeWorkArea.Y;
            minMaxInfo.MaxSize.X = relativeWorkArea.Width;
            minMaxInfo.MaxSize.Y = relativeWorkArea.Height;
            minMaxInfo.MaxTrackSize.X = relativeWorkArea.Width;
            minMaxInfo.MaxTrackSize.Y = relativeWorkArea.Height;
            Marshal.StructureToPtr(minMaxInfo, lParam, true);
            handled = true;
            return IntPtr.Zero;
        }

        private static Int32Rect ToInt32Rect(NativeRect rect)
        {
            return new Int32Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }

        public void Dispose()
        {
            _window.SourceInitialized -= Window_SourceInitialized;
            _window.Closed -= Window_Closed;
            if (_source != null)
            {
                _source.RemoveHook(WindowProc);
                _source = null;
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MinMaxInfo
        {
            public NativePoint Reserved;
            public NativePoint MaxSize;
            public NativePoint MaxPosition;
            public NativePoint MinTrackSize;
            public NativePoint MaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MonitorInfo
        {
            public int Size;
            public NativeRect Monitor;
            public NativeRect Work;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
