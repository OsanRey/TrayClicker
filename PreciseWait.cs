using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TrayClicker;

// High-resolution waitable timers work without relying on the hidden-window timeBeginPeriod policy.
internal sealed class PreciseWait : IDisposable
{
    private readonly SafeWaitHandle timer;
    private readonly IntPtr[] handles;

    public PreciseWait(WaitHandle cancel)
    {
        timer = CreateWaitableTimerEx(IntPtr.Zero, null, 2, 0x001F0003);
        if (timer.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            timer.Dispose();
            throw new Win32Exception(error, "Не удалось создать точный таймер (нужен Windows 10 1803 или новее).");
        }
        handles = [cancel.SafeWaitHandle.DangerousGetHandle(), timer.DangerousGetHandle()];
    }

    public bool Wait(double seconds)
    {
        long due = -(long)Math.Max(1, Math.Ceiling(seconds * 10_000_000));
        if (!SetWaitableTimerEx(timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        uint result = WaitForMultipleObjects(2, handles, false, uint.MaxValue);
        if (result == 0) return false;
        if (result == 1) return true;
        throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public void Dispose() => timer.Dispose();

    [DllImport("kernel32.dll", EntryPoint = "CreateWaitableTimerExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeWaitHandle CreateWaitableTimerEx(IntPtr attributes, string? name, uint flags, uint access);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimerEx(SafeWaitHandle timer, ref long due, int period,
        IntPtr callback, IntPtr argument, IntPtr wakeContext, uint tolerableDelay);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForMultipleObjects(uint count, IntPtr[] handles,
        [MarshalAs(UnmanagedType.Bool)] bool waitAll, uint milliseconds);
}
