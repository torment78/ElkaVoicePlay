using System.Runtime.InteropServices;
using System.Text;

namespace ElkaVoicePlay.Input;

internal static class NativeMethods
{
    internal delegate nint HookProc(int code, nint wParam, nint lParam);
    internal delegate void WinEventProc(nint hook, uint eventType, nint hwnd, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(nint hwnd, StringBuilder name, int maximum);
    [DllImport("user32.dll")] internal static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SendMessageTimeoutW")]
    internal static extern nint SendTextMessage(nint hwnd, uint message, nuint wParam, string text, uint flags, uint timeout, out nuint result);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetWindowsHookEx(int id, HookProc callback, nint module, uint thread);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] internal static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern nint GetModuleHandle(string? module);
    [DllImport("user32.dll")] internal static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventProc callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll", SetLastError = true)] internal static extern uint SendInput(uint count, NativeInput[] inputs, int size);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] internal static extern uint MapVirtualKey(uint code, uint mapType);
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)]
    internal static extern int GetMessage(out ThreadMessage message, nint window, uint min, uint max);
    [DllImport("user32.dll", EntryPoint = "PeekMessageW")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PeekMessage(out ThreadMessage message, nint window, uint min, uint max, uint remove);
    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostThreadMessage(uint thread, uint message, nuint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)] internal struct ThreadMessage
    {
        internal nint Window;
        internal uint Message;
        internal nuint WParam;
        internal nint LParam;
        internal uint Time;
        internal int X, Y;
        internal uint Private;
    }

    [StructLayout(LayoutKind.Sequential)] internal struct MouseHookData
    {
        internal int X, Y;
        internal uint MouseData, Flags, Time;
        internal nuint Extra;
    }
    [StructLayout(LayoutKind.Explicit, Size = 40)] internal struct NativeInput
    {
        [FieldOffset(0)] internal uint Type;
        [FieldOffset(8)] internal KeyboardInput Keyboard;
        [FieldOffset(8)] internal MouseInput Mouse;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct KeyboardInput
    {
        internal ushort Key, Scan;
        internal uint Flags, Time;
        internal nuint Extra;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseInput
    {
        internal int X, Y;
        internal uint Data, Flags, Time;
        internal nuint Extra;
    }

    [StructLayout(LayoutKind.Sequential)] internal struct KeyboardHookData
    {
        internal uint Key, Scan, Flags, Time;
        internal nuint Extra;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct GuiThreadInfo
    {
        internal uint Size, Flags;
        internal nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        internal int Left, Top, Right, Bottom;
    }
}
