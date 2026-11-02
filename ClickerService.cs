using System.Diagnostics;

namespace TrayClicker;

internal readonly record struct ClickerStatus(bool Enabled, bool Active, bool Suspended, bool Armed,
    long LeftCount, long RightCount, string? Error);
internal readonly record struct ClickerInput(bool Key, bool Escape, bool Left, bool Right, int LeftPress, int RightPress);

internal sealed class ClickerService : IDisposable
{
    private readonly object gate = new();
    private readonly ClickSchedule schedule;
    private readonly Thread worker;
    private readonly ManualResetEventSlim quitting = new();
    private readonly PhysicalMouse? mouse;
    private readonly PreciseWait preciseWait;
    private readonly Func<int, ClickerInput>? readInput;
    private readonly Action<MouseButton> click;
    private readonly bool legacyWait;
    private ClickerSettings settings;
    private bool enabled = true;
    private bool suspended = true;
    private string? error;
    private int lastLeftPress;
    private int lastRightPress;
    private bool workerFailed;

    public ClickerService(ClickerSettings settings, PhysicalMouse? mouse = null,
        Func<int, ClickerInput>? readInput = null, Action<MouseButton>? click = null, bool legacyWait = false)
    {
        this.mouse = mouse;
        this.readInput = readInput;
        this.click = click ?? NativeInput.Click;
        this.legacyWait = legacyWait;
        preciseWait = new(quitting.WaitHandle);
        lastLeftPress = mouse?.LeftPress ?? 0;
        lastRightPress = mouse?.RightPress ?? 0;
        this.settings = settings;
        schedule = new(settings);
        worker = new Thread(Run) { IsBackground = true, Name = "Mouse click scheduler" };
        worker.Start();
    }

    public ClickerStatus Status
    {
        get { lock (gate) return new(enabled, schedule.Active, suspended, schedule.Armed, schedule.LeftCount, schedule.RightCount, error); }
    }

    public void Configure(ClickerSettings value)
    {
        lock (gate) { settings = value.Validated(); schedule.Configure(settings); mouse?.SetSuppression(false, false); if (!workerFailed) error = null; }
    }
    public void Suspend(bool value)
    {
        lock (gate) { suspended = value; schedule.Stop(); mouse?.SetSuppression(false, false); }
    }
    public void SetEnabled(bool value)
    {
        lock (gate) { enabled = value && !workerFailed; schedule.Stop(); mouse?.SetSuppression(false, false); if (enabled) error = null; }
    }
    public void Stop() { lock (gate) { schedule.Stop(); mouse?.SetSuppression(false, false); } }

    private void Run()
    {
        var clock = Stopwatch.StartNew();
        bool timerRaised = false;
        try
        {
            while (!quitting.IsSet)
            {
                bool active;
                double nextDue;
                lock (gate)
                {
                    try
                    {
                        var input = readInput?.Invoke(settings.Hotkey) ?? new ClickerInput(
                            NativeInput.IsDown(settings.Hotkey), NativeInput.IsDown((int)Keys.Escape),
                            mouse?.LeftHeld ?? false, mouse?.RightHeld ?? false, mouse?.LeftPress ?? 0, mouse?.RightPress ?? 0);
                        int leftPress = input.LeftPress;
                        int rightPress = input.RightPress;
                        bool leftPressed = leftPress != lastLeftPress;
                        bool rightPressed = rightPress != lastRightPress;
                        lastLeftPress = leftPress;
                        lastRightPress = rightPress;
                        schedule.Tick(clock.Elapsed.TotalSeconds, input.Key, input.Escape,
                            enabled && !suspended && error == null, click, input.Left, input.Right, leftPressed, rightPressed);
                    }
                    catch (Exception ex)
                    {
                        schedule.Stop();
                        enabled = false;
                        error = ex.Message;
                    }
                    if (readInput == null) mouse?.SetSuppression(
                        schedule.Armed && settings.ClickMode != ClickMode.Automatic && settings.LeftEnabled,
                        schedule.Armed && settings.ClickMode != ClickMode.Automatic && settings.RightEnabled);
                    active = schedule.Armed;
                    nextDue = schedule.NextDue;
                }
                if (legacyWait)
                {
                    if (active && !timerRaised) timerRaised = NativeInput.timeBeginPeriod(1) == 0;
                    if (!active && timerRaised) { NativeInput.timeEndPeriod(1); timerRaised = false; }
                    quitting.Wait(active ? 1 : 8);
                }
                else
                {
                    double wait = Math.Min(active ? .002 : .008, nextDue - clock.Elapsed.TotalSeconds);
                    if (wait > 0) preciseWait.Wait(wait);
                }
            }
        }
        catch (Exception ex)
        {
            lock (gate)
            {
                workerFailed = true; enabled = false; schedule.Stop(); mouse?.SetSuppression(false, false);
                error = "Ошибка таймера. Перезапустите кликер.\n" + ex.Message;
            }
        }
        finally { if (timerRaised) NativeInput.timeEndPeriod(1); }
    }

    public void Dispose()
    {
        lock (gate) { enabled = false; schedule.Stop(); mouse?.SetSuppression(false, false); }
        quitting.Set();
        worker.Join();
        preciseWait.Dispose();
        quitting.Dispose();
    }
}
