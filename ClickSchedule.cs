namespace TrayClicker;

// Pure state machine. The keyboard grants permission; physical mouse presses start bursts.
internal sealed class ClickSchedule
{
    private ClickerSettings settings;
    private bool wasDown;
    private bool waitForRelease = true;
    private double nextLeft;
    private double nextRight;
    private bool leftWasHeld;
    private bool rightWasHeld;
    private bool leftBurst;
    private bool rightBurst;
    public bool Armed { get; private set; }
    public bool Active { get; private set; }
    public long LeftCount { get; private set; }
    public long RightCount { get; private set; }
    public double NextDue => !Armed ? double.PositiveInfinity : settings.ClickMode == ClickMode.Count
        ? Math.Min(leftBurst ? nextLeft : double.PositiveInfinity, rightBurst ? nextRight : double.PositiveInfinity)
        : Math.Min(leftWasHeld ? nextLeft : double.PositiveInfinity, rightWasHeld ? nextRight : double.PositiveInfinity);

    private static double Advance(double previous, double now, decimal cps)
    {
        double period = 1.0 / (double)cps;
        double next = previous + period;
        // Preserve the time grid across small wake-up delays, but discard a full missed interval.
        return next > now ? next : now + period;
    }

    public ClickSchedule(ClickerSettings settings) => this.settings = settings.Validated();
    public void Configure(ClickerSettings value) { Stop(); settings = value.Validated(); }
    public void Stop()
    {
        Active = Armed = false;
        leftBurst = rightBurst = false;
        leftWasHeld = rightWasHeld = false;
        waitForRelease = true;
    }

    public void Tick(double now, bool keyDown, bool escapeDown, bool allowed, Action<MouseButton> click,
        bool leftHeld = false, bool rightHeld = false, bool leftPressed = false, bool rightPressed = false)
    {
        if (!allowed || escapeDown)
        {
            Stop();
            waitForRelease = keyDown;
            wasDown = keyDown;
            return;
        }
        if (waitForRelease)
        {
            if (!keyDown) waitForRelease = false;
            wasDown = keyDown;
            return;
        }
        bool pressed = keyDown && !wasDown;
        wasDown = keyDown;
        bool toggleActivation = settings.ClickMode == ClickMode.Automatic || settings.Mode == ActivationMode.Toggle;
        if (!toggleActivation && !keyDown)
        {
            Active = Armed = leftBurst = rightBurst = false;
            leftWasHeld = rightWasHeld = false;
        }
        if (pressed)
        {
            if (Armed && toggleActivation)
            {
                Active = Armed = leftBurst = rightBurst = false;
                leftWasHeld = rightWasHeld = false;
            }
            else if (!Armed)
            {
                Armed = true;
                LeftCount = RightCount = 0;
                nextLeft = nextRight = now;
                leftWasHeld = rightWasHeld = false;
            }
        }
        if (!Armed) return;

        if (settings.ClickMode is ClickMode.MouseHold or ClickMode.Automatic)
        {
            bool automatic = settings.ClickMode == ClickMode.Automatic;
            bool leftRunning = settings.LeftEnabled && (automatic || leftHeld);
            bool rightRunning = settings.RightEnabled && (automatic || rightHeld);
            Active = leftRunning || rightRunning;
            if (leftRunning && (!leftWasHeld || now >= nextLeft))
            {
                click(MouseButton.Left);
                LeftCount++;
                nextLeft = Advance(leftWasHeld ? nextLeft : now, now, settings.LeftCps);
            }
            if (rightRunning && (!rightWasHeld || now >= nextRight))
            {
                click(MouseButton.Right);
                RightCount++;
                nextRight = Advance(rightWasHeld ? nextRight : now, now, settings.RightCps);
            }
            leftWasHeld = leftRunning;
            rightWasHeld = rightRunning;
            return;
        }

        // A new real press starts one series; holding does not repeat it. No backlog is queued.
        if (leftPressed && settings.LeftEnabled && !leftBurst)
        {
            leftBurst = true;
            LeftCount = 0;
            nextLeft = now;
        }
        if (rightPressed && settings.RightEnabled && !rightBurst)
        {
            rightBurst = true;
            RightCount = 0;
            nextRight = now;
        }
        if (leftBurst && now >= nextLeft)
        {
            click(MouseButton.Left);
            LeftCount++;
            nextLeft = Advance(nextLeft, now, settings.LeftCps);
            if (LeftCount >= settings.ClickLimit) leftBurst = false;
        }
        if (rightBurst && now >= nextRight)
        {
            click(MouseButton.Right);
            RightCount++;
            nextRight = Advance(nextRight, now, settings.RightCps);
            if (RightCount >= settings.ClickLimit) rightBurst = false;
        }
        Active = leftBurst || rightBurst;
    }
}
