using System.ComponentModel;
using System.Runtime.InteropServices;

namespace TrayClicker;

// GetAsyncKeyState changes after synthetic button-up. Track real mouse events instead.
internal sealed class PhysicalMouse : IDisposable
{
    private readonly HookCallback callback;
    private readonly Thread thread;
    private readonly PhysicalButtonState state = new();
    private uint threadId;
    private IntPtr hook;
    public bool LeftHeld => state.LeftHeld;
    public bool RightHeld => state.RightHeld;
    public int LeftPress => state.LeftPress;
    public int RightPress => state.RightPress;
    public void SetSuppression(bool left, bool right) => state.SetSuppression(left, right);

    public PhysicalMouse()
    {
        callback = OnMouse;
        using var ready = new ManualResetEventSlim();
        Exception? startupError = null;
        thread = new Thread(() =>
        {
            try
            {
                threadId = GetCurrentThreadId();
                PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
                if (NativeInput.IsDown((int)Keys.LButton)) state.Update(0x0201, 0);
                if (NativeInput.IsDown((int)Keys.RButton)) state.Update(0x0204, 0);
                hook = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0);
                if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось отслеживать удержание кнопок мыши.");
            }
            catch (Exception ex) { startupError = ex; }
            finally { ready.Set(); }
            if (startupError != null) return;
            try { Application.Run(); }
            finally { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
        }) { IsBackground = true, Name = "Physical mouse events" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        if (startupError != null) { thread.Join(); throw startupError; }
    }

    private IntPtr OnMouse(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            int kind = (int)message;
            if (kind is 0x0201 or 0x0202 or 0x0204 or 0x0205)
            {
                uint flags = Marshal.PtrToStructure<MouseHookData>(data).Flags;
                if (state.Update(kind, flags)) return new IntPtr(1);
            }
        }
        return CallNextHookEx(hook, code, message, data);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookData
    {
        public int X, Y;
        public uint MouseData, Flags, Time;
        public UIntPtr ExtraInfo;
    }
    private delegate IntPtr HookCallback(int code, IntPtr message, IntPtr data);
    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr Window;
        public uint Kind;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X, Y;
        public uint Private;
    }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int id, HookCallback callback, IntPtr module, uint threadId);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out Message message, IntPtr window, uint min, uint max, uint remove);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint threadId, uint message, UIntPtr wParam, IntPtr lParam);

    public void Dispose()
    {
        if (!thread.IsAlive) return;
        PostThreadMessage(threadId, 0x0012, UIntPtr.Zero, IntPtr.Zero);
        thread.Join();
        GC.KeepAlive(callback);
    }
}

internal sealed class PhysicalButtonState
{
    private volatile bool left;
    private volatile bool right;
    private volatile bool suppressLeft;
    private volatile bool suppressRight;
    private bool swallowedLeftDown;
    private bool swallowedRightDown;
    private int leftPress;
    private int rightPress;
    public bool LeftHeld => left;
    public bool RightHeld => right;
    public int LeftPress => Volatile.Read(ref leftPress);
    public int RightPress => Volatile.Read(ref rightPress);
    public void SetSuppression(bool left, bool right) { suppressLeft = left; suppressRight = right; }
    public bool Update(int message, uint flags)
    {
        if ((flags & 1) != 0) return false;
        switch (message)
        {
            case 0x0201: left = true; break;
            case 0x0202: left = false; break;
            case 0x0204: right = true; break;
            case 0x0205: right = false; break;
        }
        // Replace the physical click with exactly N generated clicks, and pair releases.
        switch (message)
        {
            case 0x0201 when suppressLeft:
                swallowedLeftDown = true;
                Interlocked.Increment(ref leftPress);
                return true;
            case 0x0204 when suppressRight:
                swallowedRightDown = true;
                Interlocked.Increment(ref rightPress);
                return true;
            case 0x0202 when swallowedLeftDown:
                swallowedLeftDown = false;
                return true;
            case 0x0205 when swallowedRightDown:
                swallowedRightDown = false;
                return true;
            default: return false;
        }
    }
}
