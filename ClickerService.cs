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
    private readonly Action<MouseButton> startClick;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly ClickPulse leftPulse;
    private readonly ClickPulse rightPulse;
    private readonly bool legacyWait;
    private ClickerSettings settings;
    private bool enabled = true;
    private bool suspended = true;
    private string? error;
    private int lastLeftPress;
    private int lastRightPress;
    private bool workerFailed;

    public ClickerService(ClickerSettings settings, PhysicalMouse? mouse = null,
        Func<int, ClickerInput>? readInput = null, Action<MouseButton>? click = null, bool legacyWait = false,
        Action<MouseButton, bool>? setButton = null)
    {
        this.mouse = mouse;
        this.readInput = readInput;
        this.click = click ?? NativeInput.Click;
        startClick = StartClick;
        leftPulse = new(MouseButton.Left, () => clock.Elapsed.TotalSeconds, setButton ?? NativeInput.SetButton);
        rightPulse = new(MouseButton.Right, () => clock.Elapsed.TotalSeconds, setButton ?? NativeInput.SetButton);
        this.legacyWait = legacyWait;
        preciseWait = new(quitting.WaitHandle);
        lastLeftPress = mouse?.LeftPress ?? 0;
        lastRightPress = mouse?.RightPress ?? 0;
        this.settings = settings.Validated();
        schedule = new(settings);
        worker = new Thread(Run) { IsBackground = true, Name = "Mouse click scheduler" };
        worker.Start();
    }

    public ClickerStatus Status
    {
        get { lock (gate) return new(enabled, schedule.Active || (schedule.Armed && (leftPulse.IsDown || rightPulse.IsDown)),
            suspended, schedule.Armed, schedule.LeftCount, schedule.RightCount, error); }
    }

    public void Configure(ClickerSettings value)
    {
        lock (gate)
        {
            if (!workerFailed) error = null;
            StopClicks();
            settings = value.Validated();
            schedule.Configure(settings);
        }
    }
    public void Suspend(bool value)
    {
        lock (gate) { suspended = value; StopClicks(); }
    }
    public void SetEnabled(bool value)
    {
        lock (gate)
        {
            enabled = value && !workerFailed;
            if (enabled) error = null;
            StopClicks();
        }
    }
    public void Stop() { lock (gate) StopClicks(); }

    private void StartClick(MouseButton button)
    {
        if (settings.PressMilliseconds == 0) click(button);
        else (button == MouseButton.Left ? leftPulse : rightPulse).Press(settings.PressMilliseconds);
    }

    private void StopClicks()
    {
        schedule.Stop();
        ReleaseButtons();
    }

    private void ReleaseButtons()
    {
        mouse?.SetSuppression(false, false);
        StopPulse(leftPulse);
        StopPulse(rightPulse);
    }

    private void StopPulse(ClickPulse pulse)
    {
        try { pulse.Stop(); }
        catch (Exception ex)
        {
            enabled = false;
            error ??= ex.Message;
        }
    }

    private void Run()
    {
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
                        double now = clock.Elapsed.TotalSeconds;
                        if (now >= leftPulse.ReleaseAt) leftPulse.Release();
                        if (now >= rightPulse.ReleaseAt) rightPulse.Release();
                        schedule.Tick(clock.Elapsed.TotalSeconds, input.Key, input.Escape,
                            enabled && !suspended && error == null, startClick, input.Left, input.Right, leftPressed, rightPressed,
                            leftPulse.ReadyAt, rightPulse.ReadyAt);
                        if (!schedule.Armed && (leftPulse.IsDown || rightPulse.IsDown)) ReleaseButtons();
                    }
                    catch (Exception ex)
                    {
                        enabled = false;
                        error = ex.Message;
                        StopClicks();
                    }
                    if (readInput == null) mouse?.SetSuppression(
                        schedule.Armed && settings.ClickMode != ClickMode.Automatic && settings.LeftEnabled,
                        schedule.Armed && settings.ClickMode != ClickMode.Automatic && settings.RightEnabled);
                    active = schedule.Armed;
                    nextDue = schedule.GetNextDue(leftPulse.ReadyAt, rightPulse.ReadyAt);
                    if (active) nextDue = Math.Min(nextDue, Math.Min(leftPulse.ReleaseAt, rightPulse.ReleaseAt));
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
                workerFailed = true; enabled = false;
                error = "Ошибка таймера. Перезапустите кликер.\n" + ex.Message;
                StopClicks();
            }
        }
        finally { if (timerRaised) NativeInput.timeEndPeriod(1); }
    }

    public void Dispose()
    {
        lock (gate) { enabled = false; StopClicks(); }
        quitting.Set();
        worker.Join();
        preciseWait.Dispose();
        quitting.Dispose();
    }
}
