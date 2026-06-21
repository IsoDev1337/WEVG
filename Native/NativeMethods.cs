using System.Runtime.InteropServices;

namespace WEVG.Native;

internal static class NativeMethods
{
    public const int GWL_STYLE = -16;
    public const long WS_POPUP = 0x80000000L;
    public const long WS_VISIBLE = 0x10000000L;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_FRAMECHANGED = 0x0020;
    public const uint WM_CLOSE = 0x0010;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    // Explicit W entry point: GetWindowLongPtr/SetWindowLongPtr only exist as exports on x64.
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    // 1 ms timer resolution for Thread.Sleep: without it the frame clock has ~15 ms
    // of error per frame and the video comes out with micro-stutter.
    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    public static extern uint TimeBeginPeriod(uint uMilliseconds);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    public static extern uint TimeEndPeriod(uint uMilliseconds);
}
