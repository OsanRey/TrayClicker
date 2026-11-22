using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace TrayClicker;

internal static class SelfTests
{
    private sealed class Scenario
    {
        public readonly ClickSchedule Schedule;
        public readonly List<MouseButton> Clicks = new();
        public Scenario(ClickerSettings? settings = null)
        {
            Schedule = new(settings ?? new());
            Tick(0, key: false);
        }
        public void Tick(double time, bool key = true, bool escape = false, bool allowed = true,
            bool left = false, bool right = false, bool leftPress = false, bool rightPress = false) =>
            Schedule.Tick(time, key, escape, allowed, Clicks.Add, left, right, leftPress, rightPress);
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static int Run(string? reportPath)
    {
        var report = new StringBuilder();
        int failures = 0, total = 0;
        void Test(string name, Action action)
        {
            total++;
            try { action(); report.AppendLine("PASS: " + name); }
            catch (Exception ex) { failures++; report.AppendLine("FAIL: " + name + " — " + ex.Message); }
        }
        Test("Count mode: mouse without hotkey and hotkey without mouse do not click", () =>
        {
            var s = new Scenario();
            s.Tick(.1, key: false, leftPress: true);
            s.Tick(.2);
            Check(s.Clicks.Count == 0 && s.Schedule.Armed && !s.Schedule.Active, "Permission must not click by itself");
        });
        Test("Count mode: exact N clicks, no repeat while mouse stays held", () =>
        {
            var s = new Scenario(new() { ClickLimit = 3, LeftCps = 10 });
            s.Tick(.1);
            s.Tick(.2, left: true, leftPress: true);
            s.Tick(.25, left: true);
            Check(s.Clicks.Count == 1, "Rate exceeded");
            s.Tick(.31, left: true);
            s.Tick(.42, left: true);
            s.Tick(1, left: true);
            Check(s.Clicks.Count == 3 && !s.Schedule.Active && s.Schedule.Armed, "Exact limit or persistent permission failed");
        });
        Test("Count mode: release of mouse does not interrupt a series", () =>
        {
            var s = new Scenario(new() { ClickLimit = 3 });
            s.Tick(.1);
            s.Tick(.2, leftPress: true);
            s.Tick(.31);
            s.Tick(.42);
            Check(s.Clicks.Count == 3, "A short mouse press must still produce the whole series");
        });
        Test("Count mode: next physical mouse press starts another series", () =>
        {
            var s = new Scenario(new() { ClickLimit = 1 });
            s.Tick(.1);
            s.Tick(.2, leftPress: true);
            s.Tick(.3);
            s.Tick(.4, leftPress: true);
            Check(s.Clicks.Count == 2 && s.Schedule.LeftCount == 1 && s.Schedule.Armed, "Next series failed");
        });
        Test("Count mode: presses during an existing series do not add a backlog", () =>
        {
            var s = new Scenario(new() { ClickLimit = 2, LeftCps = 1 });
            s.Tick(.1);
            s.Tick(.2, leftPress: true);
            s.Tick(.3, leftPress: true);
            s.Tick(1.3);
            s.Tick(3);
            Check(s.Clicks.Count == 2 && !s.Schedule.Active, "Repeated presses queued an unwanted series");
        });
        Test("Count mode: left and right series have independent speeds and triggers", () =>
        {
            var s = new Scenario(new() { LeftEnabled = true, RightEnabled = true, LeftCps = 2, RightCps = 4, ClickLimit = 3 });
            s.Tick(.1);
            s.Tick(.2, leftPress: true);
            Check(s.Schedule.LeftCount == 1 && s.Schedule.RightCount == 0, "Left press must not trigger right");
            s.Tick(.3, rightPress: true);
            s.Tick(.55);
            Check(s.Schedule.LeftCount == 1 && s.Schedule.RightCount == 2, "Independent rates failed");
            s.Tick(.71); s.Tick(.81); s.Tick(1.22);
            Check(s.Schedule.LeftCount == 3 && s.Schedule.RightCount == 3 && !s.Schedule.Active, "Both exact limits failed");
        });
        Test("Count mode: unselected mouse button is ignored", () =>
        {
            var s = new Scenario(new() { LeftEnabled = false, RightEnabled = true, ClickLimit = 1 });
            s.Tick(.1); s.Tick(.2, leftPress: true); s.Tick(.3, rightPress: true);
            Check(s.Clicks.SequenceEqual(new[] { MouseButton.Right }), "Unselected button clicked");
        });
        Test("Holding hotkey: release interrupts series and requires a new mouse press", () =>
        {
            var s = new Scenario(new() { ClickLimit = 3 });
            s.Tick(.1); s.Tick(.2, leftPress: true); s.Tick(.31, key: false);
            s.Tick(.4); s.Tick(.6, left: true);
            Check(s.Clicks.Count == 1 && !s.Schedule.Active, "Key release did not cancel or old mouse hold restarted count");
        });
        Test("Toggle hotkey: repeats ignored, permission survives release, next key press stops", () =>
        {
            var s = new Scenario(new() { Mode = ActivationMode.Toggle });
            s.Tick(.1); s.Tick(.2); s.Tick(.3, key: false, leftPress: true);
            Check(s.Clicks.Count == 1 && s.Schedule.Armed, "Key release or repeat revoked toggle permission");
            s.Tick(.4);
            Check(s.Clicks.Count == 1 && !s.Schedule.Armed, "Second key press must stop before another click");
        });
        Test("Mouse hold: requires hotkey permission plus selected mouse button", () =>
        {
            var s = new Scenario(new() { ClickMode = ClickMode.MouseHold });
            s.Tick(.1, key: false, left: true); s.Tick(.2); s.Tick(.3, right: true);
            Check(s.Clicks.Count == 0 && s.Schedule.Armed, "Single condition or wrong button clicked");
            s.Tick(.4, left: true); s.Tick(.51, left: true); s.Tick(.7);
            Check(s.Clicks.Count == 2 && !s.Schedule.Active && s.Schedule.Armed, "Mouse release must stop clicks, keep permission");
            s.Tick(.8, left: true); s.Tick(1, key: false, left: true);
            Check(s.Clicks.Count == 3 && !s.Schedule.Armed, "Re-hold or keyboard release failed");
        });
        Test("Mouse hold: toggle permission survives separate mouse holds", () =>
        {
            var s = new Scenario(new() { ClickMode = ClickMode.MouseHold, Mode = ActivationMode.Toggle });
            s.Tick(.1); s.Tick(.2, key: false, left: true); s.Tick(.4, key: false);
            s.Tick(.5, key: false, left: true);
            Check(s.Clicks.Count == 2 && s.Schedule.Active, "Separate holds did not resume");
            s.Tick(.7, left: true);
            Check(s.Clicks.Count == 2 && !s.Schedule.Armed, "Toggle off failed");
        });
        Test("Mouse hold: both buttons run and release independently", () =>
        {
            var s = new Scenario(new() { ClickMode = ClickMode.MouseHold, RightEnabled = true, LeftCps = 2, RightCps = 4 });
            s.Tick(.1, left: true, right: true); s.Tick(.35, left: true, right: true);
            Check(s.Schedule.LeftCount == 1 && s.Schedule.RightCount == 2, "Independent rates failed");
            s.Tick(.6, right: true);
            Check(s.Schedule.LeftCount == 1 && s.Schedule.RightCount == 3 && s.Schedule.Active, "Left release stopped right");
            s.Tick(.9);
            Check(!s.Schedule.Active, "Both released must stop");
        });
        Test("Mouse hold: count value does not limit continuous clicks", () =>
        {
            var s = new Scenario(new() { ClickMode = ClickMode.MouseHold, ClickLimit = 1, LeftCps = 500 });
            for (int i = 0; i < 10_000; i++) s.Tick(i * .003, left: true);
            Check(s.Schedule.LeftCount == 10_000 && s.Schedule.Active, "Continuous mode was limited");
        });
        Test("Escape cancels both modes and blocks held hotkey from restarting", () =>
        {
            foreach (var mode in new[] { ClickMode.Count, ClickMode.MouseHold })
            {
                var s = new Scenario(new() { ClickMode = mode, Mode = ActivationMode.Toggle });
                s.Tick(.1); s.Tick(.2, left: true, leftPress: true);
                s.Tick(.3, escape: true, left: true); s.Tick(.4, left: true, leftPress: true);
                Check(s.Clicks.Count == 1 && !s.Schedule.Armed, "Escape failed for " + mode);
            }
        });
        Test("Pause, reconfiguration, startup and manual stop require a fresh hotkey press", () =>
        {
            var s = new Scenario(new() { ClickMode = ClickMode.MouseHold });
            s.Tick(.1, left: true); s.Tick(.2, allowed: false, left: true);
            s.Tick(.3, left: true);
            Check(s.Clicks.Count == 1 && !s.Schedule.Armed, "Resume used held key");
            s.Tick(.4, key: false); s.Tick(.5, left: true); s.Schedule.Stop(); s.Tick(.6, left: true);
            Check(s.Clicks.Count == 2 && !s.Schedule.Armed, "Manual stop restarted");
            s.Schedule.Configure(new() { ClickMode = ClickMode.MouseHold, Hotkey = (int)Keys.F8 });
            s.Tick(.7, left: true);
            Check(s.Clicks.Count == 2, "Reconfiguration accepted pre-held key");
            var startup = new ClickSchedule(new() { ClickMode = ClickMode.MouseHold });
            startup.Tick(0, true, false, true, _ => throw new Exception("Startup clicked"), leftHeld: true);
        });
        Test("Late scheduling tick does not catch up with a burst", () =>
        {
            var s = new Scenario(new() { ClickLimit = 100, LeftCps = 500 });
            s.Tick(.1); s.Tick(.2, leftPress: true); s.Tick(30);
            Check(s.Clicks.Count == 2, "A stall caused a click backlog");
        });
        Test("Fractional speed means one click every ten seconds", () =>
        {
            var s = new Scenario(new() { LeftCps = .1m });
            s.Tick(.1); s.Tick(.2, leftPress: true); s.Tick(9); s.Tick(10.2);
            Check(s.Clicks.Count == 2, "Fractional speed failed");
        });
        Test("Small wake-up lateness does not accumulate into rate drift", () =>
        {
            var s = new Scenario(new() { ClickMode = ClickMode.MouseHold, LeftCps = 200 });
            s.Tick(0, left: true);
            for (int i = 1; i <= 100; i++) s.Tick(i * .005 + .0008, left: true);
            Check(s.Clicks.Count == 101 && Math.Abs(s.Schedule.NextDue - .505) < .000001,
                "Wake-up lateness shifted the time grid");
            s.Tick(5, left: true);
            s.Tick(5, left: true);
            Check(s.Clicks.Count == 102 && s.Schedule.NextDue > 5, "A long stall caused a catch-up burst");
        });
        Test("Settings range validation and JSON round-trip", () =>
        {
            var v = new ClickerSettings { Hotkey = (int)Keys.Escape, Mode = (ActivationMode)99, ClickMode = (ClickMode)99,
                LeftCps = -1, RightCps = 9_999, ClickLimit = 0, PressMilliseconds = 250, LeftEnabled = false, RightEnabled = false }.Validated();
            Check(v.Hotkey == (int)Keys.F6 && v.Mode == ActivationMode.Hold && v.ClickMode == ClickMode.Count && v.LeftCps == .1m
                && v.RightCps == 500 && v.ClickLimit == 1 && v.PressMilliseconds == 200 && v.LeftEnabled, "Invalid preferences passed through");
            var original = new ClickerSettings { Hotkey = (int)Keys.F8, Mode = ActivationMode.Toggle, ClickMode = ClickMode.MouseHold,
                LeftEnabled = false, RightEnabled = true, RightCps = 17.5m, ClickLimit = 31, PressMilliseconds = 35 };
            Check(JsonSerializer.Deserialize<ClickerSettings>(JsonSerializer.Serialize(original)) == original, "JSON changed settings");
            Check(JsonSerializer.Deserialize<ClickerSettings>("{\"LeftCps\":15}")?.PressMilliseconds == 20,
                "Older settings must receive the default duration");
        });
        Test("Timed count mode: hold, release, and wait before pressing again", () =>
        {
            var schedule = new ClickSchedule(new() { LeftCps = 500, ClickLimit = 2 });
            var transitions = new List<bool>();
            double now = 0;
            var pulse = new ClickPulse(MouseButton.Left, () => now, (_, down) => transitions.Add(down));
            void Tick(double time, bool key = true, bool leftPress = false)
            {
                now = time;
                if (now >= pulse.ReleaseAt) pulse.Release();
                schedule.Tick(now, key, false, true, _ => pulse.Press(20), leftPressed: leftPress, leftReady: pulse.ReadyAt);
            }
            Tick(0, key: false);
            Tick(.01, leftPress: true);
            Tick(.029);
            Check(transitions.SequenceEqual([true]), "Click released early or pressed twice");
            Tick(.040); // Late wake-up: measure the up interval from the actual release.
            Tick(.0405);
            Check(transitions.SequenceEqual([true, false]) && Math.Abs(schedule.GetNextDue(pulse.ReadyAt, 0) - .041) < .000001,
                "A late release shortened the up interval");
            Tick(.0415);
            Tick(.062);
            Tick(.120);
            Check(transitions.SequenceEqual([true, false, true, false]) && schedule.LeftCount == 2 && !pulse.IsDown,
                "The final click must complete after the series reaches its count");
        });
        Test("Timed button cancellation sends one matching release", () =>
        {
            var transitions = new List<(MouseButton, bool)>();
            var pulse = new ClickPulse(MouseButton.Right, () => 0, (button, down) => transitions.Add((button, down)));
            pulse.Press(50);
            pulse.Stop();
            pulse.Stop();
            Check(transitions.SequenceEqual([(MouseButton.Right, true), (MouseButton.Right, false)]) && pulse.ReadyAt == 0,
                "Cancellation must release the button once and clear the delay");
        });
        Test("100 CPS with 5 ms presses keeps the 10 ms grid despite timer jitter", () =>
        {
            var schedule = new ClickSchedule(new() { ClickMode = ClickMode.Automatic, LeftCps = 100, PressMilliseconds = 5 });
            double now = 0;
            double lastDown = 0;
            double firstDown = 0;
            double lastUp = -1;
            int count = 0;
            var pulse = new ClickPulse(MouseButton.Left, () => now, (_, down) =>
            {
                if (down)
                {
                    Check(now - lastUp >= .001 - .000001, "Released interval was too short");
                    if (count++ == 0) firstDown = now;
                    lastDown = now;
                }
                else
                {
                    Check(now - lastDown >= .005 - .000001, "Hold duration was too short");
                    lastUp = now;
                }
            });
            void Tick(double time, bool key = true)
            {
                now = time;
                if (now >= pulse.ReleaseAt) pulse.Release();
                schedule.Tick(now, key, false, true, _ => pulse.Press(5), leftReady: pulse.ReadyAt);
            }
            Tick(0, key: false);
            Tick(.01);
            for (int i = 1; i <= 1500; i++)
            {
                Tick(i * .01 + .0058);
                if (i < 1500) Tick(i * .01 + .0106);
            }
            Check(count == 1500 && !pulse.IsDown && Math.Abs(lastDown - firstDown - 14.99) < .001,
                "Late wakes reduced 100 CPS below its target");
        });
        Test("Worker sends timed transitions and Escape releases an active click", () =>
        {
            using var idleRead = new ManualResetEventSlim();
            using var pressed = new ManualResetEventSlim();
            using var released = new ManualResetEventSlim();
            int input = 0;
            int releases = 0;
            using var service = new ClickerService(new() { ClickMode = ClickMode.Automatic, PressMilliseconds = 200 },
                readInput: _ =>
                {
                    int state = Volatile.Read(ref input);
                    if (state == 0) idleRead.Set();
                    return new(state == 1, state == 2, false, false, 0, 0);
                },
                setButton: (_, down) =>
                {
                    if (down) pressed.Set();
                    else
                    {
                        Interlocked.Increment(ref releases);
                        released.Set();
                    }
                });
            service.Suspend(false);
            Check(idleRead.Wait(1000), "Worker did not read idle input");
            _ = service.Status; // Wait for the observed idle tick to finish.
            Volatile.Write(ref input, 1);
            Check(pressed.Wait(1000), "Worker did not send a timed press");
            Volatile.Write(ref input, 2);
            Check(released.Wait(1000), "Worker did not release the button");
            Check(!service.Status.Armed && service.Status.LeftCount == 1 && releases == 1 && service.Status.Error == null,
                "Escape did not stop timed output cleanly");
        });
        Test("Native INPUT layout and complete down/up pairs", () =>
        {
            Check(Marshal.SizeOf<NativeInput.Input>() == (IntPtr.Size == 8 ? 40 : 28), "Wrong INPUT size");
            Check(Marshal.OffsetOf<NativeInput.Input>(nameof(NativeInput.Input.Data)).ToInt32() == (IntPtr.Size == 8 ? 8 : 4), "Wrong union alignment");
            var l = NativeInput.CreatePair(MouseButton.Left); var r = NativeInput.CreatePair(MouseButton.Right);
            Check(l[0].Type == 0 && l[0].Data.Mouse.Flags == 2 && l[1].Data.Mouse.Flags == 4
                && r[0].Data.Mouse.Flags == 8 && r[1].Data.Mouse.Flags == 16, "Incomplete button pairs");
        });
        Test("Injected clicks do not change real hold state or generate triggers", () =>
        {
            var state = new PhysicalButtonState();
            state.SetSuppression(true, true);
            Check(state.Update(0x0201, 0), "Armed physical press must be replaced");
            Check(!state.Update(0x0202, 1) && !state.Update(0x0204, 3), "Injected clicks must pass through");
            Check(state.LeftHeld && !state.RightHeld && state.LeftPress == 1 && state.RightPress == 0, "Injected clicks changed physical state");
        });
        Test("Physical release stays paired after permission is revoked", () =>
        {
            var state = new PhysicalButtonState();
            Check(!state.Update(0x0201, 0), "Idle physical click must pass through");
            state.SetSuppression(true, false);
            Check(!state.Update(0x0202, 0), "Don't swallow release of a passed-through press");
            Check(state.Update(0x0201, 0) && state.LeftPress == 1, "Armed press was not counted and replaced");
            Check(!state.Update(0x0204, 0), "Unselected right button must pass through");
            state.SetSuppression(false, false);
            Check(state.Update(0x0202, 0) && !state.LeftHeld, "Matching release must be swallowed even after disarm");
            Check(!state.Update(0x0201, 0), "Disarmed next press must pass through");
        });
        Test("Worker stays idle while disabled and shuts down", () =>
        {
            using var service = new ClickerService(new());
            service.SetEnabled(false); service.Suspend(false); Thread.Sleep(35);
            Check(!service.Status.Active && !service.Status.Armed && service.Status.LeftCount == 0 && service.Status.Error == null, "Idle worker failed");
        });
        Test("Physical-mouse hook starts its message loop and shuts down", () =>
        {
            using var mouse = new PhysicalMouse();
            Thread.Sleep(30);
        });

        Test("Side buttons validate and survive settings serialization", () =>
        {
            foreach (var key in new[] { Keys.XButton1, Keys.XButton2 })
            {
                var settings = new ClickerSettings { Hotkey = (int)key };
                var restored = JsonSerializer.Deserialize<ClickerSettings>(JsonSerializer.Serialize(settings))!.Validated();
                Check(ClickerSettings.IsValidHotkey((int)key) && restored.Hotkey == (int)key, "Side button was rejected");
            }
            Check(!ClickerSettings.IsValidHotkey((int)Keys.LButton) && !ClickerSettings.IsValidHotkey((int)Keys.RButton),
                "Click output buttons must remain unavailable as activation keys");
        });
        Test("Capture X1 and X2 over child controls and save the selected activation", () =>
        {
            using var icon = TrayApplication.CreateIcon(Color.Blue);
            ClickerSettings? saved = null;
            using var form = new SettingsForm(new(), icon, () => new(true, false, true, false, 0, 0, null), value => saved = value, () => { }, () => { });
            form.Show();
            try
            {
                foreach (int button in new[] { 1, 2 })
                {
                    var chooser = (Button)form.Controls.Find("HotkeyButton", true).Single();
                    chooser.PerformClick();
                    var down = Message.Create(chooser.Handle, 0x020B, new IntPtr(button << 16), IntPtr.Zero);
                    Check(form.PreFilterMessage(ref down), "Side-button down was not captured");
                    var up = Message.Create(chooser.Handle, 0x020C, new IntPtr(button << 16), IntPtr.Zero);
                    Check(form.PreFilterMessage(ref up), "Captured release was not consumed");
                    ((Button)form.Controls.Find("TrayButton", true).Single()).PerformClick();
                    Check(saved?.Hotkey == (int)(button == 1 ? Keys.XButton1 : Keys.XButton2), "Wrong side button saved");
                    Check(!form.PreFilterMessage(ref down), "Side button captured outside selection mode");
                }
            }
            finally { form.Exiting = true; form.Close(); }
        });
        Test("Capture Tab without navigating and consume its repeat and release", () =>
        {
            using var icon = TrayApplication.CreateIcon(Color.Blue);
            ClickerSettings? saved = null;
            using var form = new SettingsForm(new(), icon, () => new(true, false, true, false, 0, 0, null), value => saved = value, () => { }, () => { });
            form.Show();
            var chooser = (Button)form.Controls.Find("HotkeyButton", true).Single();
            chooser.PerformClick();
            var down = Message.Create(chooser.Handle, 0x0100, (IntPtr)Keys.Tab, IntPtr.Zero);
            var repeat = Message.Create(chooser.Handle, 0x0100, (IntPtr)Keys.Return, new IntPtr(1 << 30));
            var up = Message.Create(chooser.Handle, 0x0101, (IntPtr)Keys.Tab, IntPtr.Zero);
            Check(form.PreFilterMessage(ref repeat), "A repeat from the key that opened capture leaked");
            Check(form.PreFilterMessage(ref down) && form.PreFilterMessage(ref down), "Tab escaped capture or leaked its repeat");
            Check(form.PreFilterMessage(ref up) && !form.PreFilterMessage(ref down), "Tab did not return to normal navigation");
            ((Button)form.Controls.Find("TrayButton", true).Single()).PerformClick();
            Check(saved?.Hotkey == (int)Keys.Tab, "Wrong key saved");
        });
        Test("Automatic mode keeps the preferred activation across saving and reopening", () =>
        {
            using var icon = TrayApplication.CreateIcon(Color.Blue);
            ClickerSettings? saved = null;
            using var form = new SettingsForm(new(), icon, () => new(true, false, true, false, 0, 0, null), value => saved = value, () => { }, () => { });
            form.Show();
            var mode = (ComboBox)form.Controls.Find("ClickMode", true).Single();
            var activation = (ComboBox)form.Controls.Find("KeyMode", true).Single();
            Check(mode.Visible && activation.Visible, "Mode controls are unavailable");
            mode.SelectedIndex = (int)ClickMode.Automatic;
            Check(!activation.Enabled && activation.SelectedIndex == (int)ActivationMode.Toggle, "Automatic activation must display toggle");
            ((Button)form.Controls.Find("TrayButton", true).Single()).PerformClick();
            Check(saved is { ClickMode: ClickMode.Automatic, Mode: ActivationMode.Hold }, "Automatic mode overwrote the preference");
            form.LoadSettings(saved!);
            mode.SelectedIndex = (int)ClickMode.MouseHold;
            Check(activation.Enabled && activation.SelectedIndex == (int)ActivationMode.Hold, "Previous activation was not restored");
        });
        Test("Closing unchanged settings avoids writing, including on a read-only install", () =>
        {
            using var icon = TrayApplication.CreateIcon(Color.Blue);
            bool hidden = false;
            int writes = 0;
            using var form = new SettingsForm(new(), icon, () => new(true, false, true, false, 0, 0, null),
                _ => { writes++; throw new UnauthorizedAccessException(); }, () => hidden = true, () => { });
            form.Show();
            form.Close();
            Check(hidden && writes == 0, "Closing unchanged settings attempted to write");
        });
        Test("A failed save leaves the changed settings open and can be retried", () =>
        {
            bool hidden = false;
            int writes = 0;
            ClickerSettings? saved = null;
            void Save(ClickerSettings value)
            {
                if (++writes == 1) throw new IOException("Test write error");
                saved = value;
            }
            using var icon = TrayApplication.CreateIcon(Color.Blue);
            using var form = new SettingsForm(new(), icon, () => new(true, false, true, false, 0, 0, null),
                Save, () => hidden = true, () => { });
            form.Show();
            ((ComboBox)form.Controls.Find("ClickMode", true).Single()).SelectedIndex = (int)ClickMode.Automatic;
            form.Close();
            Check(!hidden && form.Visible && saved == null, "Failed save hid the window or reported success");
            form.Close();
            Check(hidden && saved?.ClickMode == ClickMode.Automatic, "Retry did not save the pending changes");
        });

        Test("Automatic mode starts on activation alone and stops on next press", () =>
        {
            foreach (var activation in new[] { ActivationMode.Hold, ActivationMode.Toggle })
            {
                var s = new Scenario(new() { ClickMode = ClickMode.Automatic, Mode = activation, ClickLimit = 1 });
                s.Tick(.1);
                s.Tick(.21);
                s.Tick(.32, key: false);
                Check(s.Clicks.Count == 3 && s.Schedule.Active, "Release or repeat interrupted automatic clicking");
                s.Tick(.43);
                Check(s.Clicks.Count == 3 && !s.Schedule.Active && !s.Schedule.Armed, "Second press did not stop");
                s.Tick(.5, key: false); s.Tick(.6);
                Check(s.Clicks.Count == 4 && s.Schedule.Active, "Restart failed");
            }
        });
        Test("Automatic mode uses each enabled button's speed and Escape stops it", () =>
        {
            var s = new Scenario(new() { ClickMode = ClickMode.Automatic, RightEnabled = true, LeftCps = 2, RightCps = 4 });
            s.Tick(0); s.Tick(.25); s.Tick(.5, key: false);
            Check(s.Schedule.LeftCount == 2 && s.Schedule.RightCount == 3, "Independent automatic rates failed");
            s.Tick(.75, key: false, escape: true); s.Tick(1, key: false);
            Check(s.Clicks.Count == 5 && !s.Schedule.Armed, "Escape did not cancel automatic clicks");
        });

        report.AppendLine($"RESULT: {total - failures}/{total} passed; {failures} failed.");
        if (reportPath != null) File.WriteAllText(reportPath, report.ToString());
        Console.Write(report.ToString());
        return failures == 0 ? 0 : 1;
    }

    public static int Preview(string path)
    {
        using var icon = TrayApplication.CreateIcon(Color.FromArgb(129, 140, 248));
        using var form = new SettingsForm(new(), icon, () => new(true, false, true, false, 0, 0, null), _ => { }, () => { }, () => { });
        form.TopMost = true;
        form.Show();
        ShowWindow(form.Handle, 1);
        form.Activate(); Application.DoEvents();
        using var bitmap = new Bitmap(form.Width, form.Height);
        void Capture()
        {
            form.Refresh(); Application.DoEvents(); Thread.Sleep(100);
            // Only capture the application's own unobscured window; otherwise render off-screen.
            bool ownWindow = GetForegroundWindow() == form.Handle && IsWindowVisible(form.Handle);
            try
            {
                if (!ownWindow) throw new InvalidOperationException("Окно предпросмотра не на переднем плане.");
                using var graphics = Graphics.FromImage(bitmap);
                graphics.CopyFromScreen(form.Location, Point.Empty, bitmap.Size);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                File.WriteAllText(path + ".render-note.txt", "Использован рендер без захвата экрана: " + ex.Message);
            }
        }
        Capture();
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        form.LoadSettings(new() { ClickMode = ClickMode.MouseHold, Mode = ActivationMode.Toggle, RightEnabled = true, RightCps = 12.5m });
        Application.DoEvents();
        Capture();
        bitmap.Save(Path.Combine(Path.GetDirectoryName(path) ?? ".", Path.GetFileNameWithoutExtension(path) + "-mouse.png"),
            System.Drawing.Imaging.ImageFormat.Png);
        form.LoadSettings(new() { ClickMode = ClickMode.Automatic, Hotkey = (int)Keys.XButton2 });
        Application.DoEvents();
        Capture();
        bitmap.Save(Path.Combine(Path.GetDirectoryName(path) ?? ".", Path.GetFileNameWithoutExtension(path) + "-automatic.png"),
            System.Drawing.Imaging.ImageFormat.Png);
        form.Exiting = true; form.Close();
        return 0;
    }

    public static int Smoke(string reportPath)
    {
        using var app = new TrayApplication(enableClicking: false);
        using var timer = new System.Windows.Forms.Timer { Interval = 350 };
        Point position = default;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var form = Application.OpenForms.OfType<SettingsForm>().Single();
            position = form.Location;
            form.WindowState = FormWindowState.Minimized;
            Check(!form.Visible, "Minimizing did not hide the window in the tray");
            app.ShowSettings();
            Check(form.Visible && form.WindowState == FormWindowState.Normal && form.Location == position,
                $"Restoring from the tray moved the window from {position} to {form.Location}");
            ((Button)form.Controls.Find("ExitButton", true).Single()).PerformClick();
        };
        timer.Start();
        Application.Run(app);
        Check(!Application.OpenForms.OfType<SettingsForm>().Any(), "Exit left the settings window open");
        File.WriteAllText(reportPath, $"PASS: startup, tray restore at ({position.X}, {position.Y}), and full shutdown through Exit.\n");
        return 0;
    }

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);
}
