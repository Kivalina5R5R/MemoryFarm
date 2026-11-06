#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
#endif

namespace Fortal.Display
{
    // Keeps every game window above other apps, so the kiosk window covers the touch launcher.
    // Each window sits on its own monitor, so this never hides one game output behind another.
    public static class GameWindowsOnTop
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private delegate bool EnumWindowsProc(IntPtr window, IntPtr data);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr data);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

        private static readonly IntPtr Topmost = new IntPtr(-1);
        private const uint NoSize = 0x0001;
        private const uint NoMove = 0x0002;
        private const uint NoActivate = 0x0010;
        private static uint currentProcessId;

        public static void Apply()
        {
            currentProcessId = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            EnumWindows(RaiseOwnWindow, IntPtr.Zero);
        }

        [AOT.MonoPInvokeCallback(typeof(EnumWindowsProc))]
        private static bool RaiseOwnWindow(IntPtr window, IntPtr data)
        {
            GetWindowThreadProcessId(window, out uint processId);
            if (processId == currentProcessId && IsWindowVisible(window))
            {
                SetWindowPos(window, Topmost, 0, 0, 0, 0, NoSize | NoMove | NoActivate);
            }
            return true;
        }
#else
        public static void Apply()
        {
        }
#endif
    }
}
