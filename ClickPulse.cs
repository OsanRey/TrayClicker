namespace TrayClicker;

internal sealed class ClickPulse(MouseButton button, Func<double> now, Action<MouseButton, bool> send)
{
    public const int MinimumReleaseMilliseconds = 1;
    private double readyAt;
    public double ReleaseAt { get; private set; } = double.PositiveInfinity;
    public bool IsDown => !double.IsPositiveInfinity(ReleaseAt);
    public double ReadyAt => IsDown ? double.PositiveInfinity : readyAt;

    public void Press(int milliseconds)
    {
        send(button, true);
        ReleaseAt = now() + milliseconds / 1000.0;
    }

    public void Release()
    {
        if (!IsDown) return;
        send(button, false);
        readyAt = now() + MinimumReleaseMilliseconds / 1000.0;
        ReleaseAt = double.PositiveInfinity;
    }

    public void Stop()
    {
        Release();
        readyAt = 0;
    }
}
