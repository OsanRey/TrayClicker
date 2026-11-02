using System.ComponentModel;
using System.Runtime.InteropServices;

namespace TrayClicker;

internal static class NativeInput
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public HardwareInput Hardware;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseInput
    {
        public int Dx, Dy;
        public uint MouseData, Flags, Time;
        public UIntPtr ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardInput
    {
        public ushort VirtualKey, Scan;
        public uint Flags, Time;
        public UIntPtr ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct HardwareInput { public uint Message; public ushort ParamLow, ParamHigh; }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("winmm.dll")]
    internal static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")]
    internal static extern uint timeEndPeriod(uint period);

    private static readonly int InputSize = Marshal.SizeOf<Input>();
    private static readonly Input[] Left = CreatePair(MouseButton.Left);
    private static readonly Input[] Right = CreatePair(MouseButton.Right);

    public static bool IsDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    internal static Input[] CreatePair(MouseButton button)
    {
        uint down = button == MouseButton.Left ? 0x0002u : 0x0008u;
        uint up = button == MouseButton.Left ? 0x0004u : 0x0010u;
        return [new() { Data = new() { Mouse = new() { Flags = down } } },
                new() { Data = new() { Mouse = new() { Flags = up } } }];
    }

    public static void Click(MouseButton button)
    {
        var pair = button == MouseButton.Left ? Left : Right;
        uint sent = SendInput(2, pair, InputSize);
        if (sent == 2) return;
        int error = Marshal.GetLastWin32Error();
        // If only down was accepted, always attempt to release the same button.
        if (sent == 1) SendInput(1, [pair[1]], InputSize);
        throw new InvalidOperationException("Windows не приняла клик. Если целевое приложение запущено от администратора, "
            + "запустите кликер с такими же правами."
            + (error == 0 ? "" : "\n" + new Win32Exception(error).Message));
    }
}
