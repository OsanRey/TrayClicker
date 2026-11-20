namespace TrayClicker;

internal sealed class TrayApplication : ApplicationContext
{
    private readonly ClickerService service;
    private readonly PhysicalMouse mouse;
    private readonly SettingsForm form;
    private readonly NotifyIcon tray;
    private readonly Icon appIcon;
    private readonly Icon activeIcon;
    private readonly Icon pausedIcon;
    private readonly ContextMenuStrip menu;
    private readonly ToolStripMenuItem enabledItem;
    private readonly ToolStripMenuItem countItem;
    private readonly System.Windows.Forms.Timer timer;
    private ClickerSettings settings;
    private Point? windowPosition;
    private Point? savedWindowPosition;
    private string? previousError;
    private bool exiting;

    public TrayApplication(bool enableClicking = true)
    {
        settings = SettingsStore.Load(out string? warning);
        appIcon = CreateIcon(Color.FromArgb(129, 140, 248));
        activeIcon = CreateIcon(Color.FromArgb(34, 197, 94));
        pausedIcon = CreateIcon(Color.FromArgb(148, 163, 184));
        mouse = new PhysicalMouse();
        service = new(settings, mouse);
        if (!enableClicking) service.SetEnabled(false);
        form = new SettingsForm(settings, appIcon, () => service.Status, Save, HideSettings, ExitThread);
        windowPosition = savedWindowPosition = SettingsStore.LoadWindowPosition();
        form.LocationChanged += (_, _) => TrackWindowPosition();
        form.ResizeEnd += (_, _) => RememberWindowPosition();
        menu = new ContextMenuStrip();
        menu.Items.Add("Настройки…", null, (_, _) => ShowSettings());
        enabledItem = new ToolStripMenuItem("Горячая клавиша включена") { Checked = true };
        enabledItem.Click += (_, _) => service.SetEnabled(!service.Status.Enabled);
        menu.Items.Add(enabledItem);
        menu.Items.Add("Остановить клики · Esc", null, (_, _) => service.Stop());
        menu.Items.Add(new ToolStripSeparator());
        countItem = new ToolStripMenuItem { Enabled = false };
        menu.Items.Add(countItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => ExitThread());
        menu.Opening += (_, _) => service.Suspend(true);
        menu.Closed += (_, _) => { if (!exiting && !form.Visible) service.Suspend(false); };
        tray = new NotifyIcon { Icon = appIcon, Text = "TrayClicker", ContextMenuStrip = menu, Visible = true };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowSettings(); };
        timer = new System.Windows.Forms.Timer { Interval = 150 };
        timer.Tick += (_, _) => UpdateTray();
        timer.Start();
        ShowSettings();
        UpdateTray();
        if (warning != null) form.ShowLoadWarning(warning);
    }

    internal static Icon CreateIcon(Color accent)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var background = new SolidBrush(Color.FromArgb(23, 37, 59));
            using var outline = new Pen(Color.White, 2);
            using var line = new Pen(accent, 2.5f);
            using var dot = new SolidBrush(accent);
            graphics.FillEllipse(background, 0, 0, 31, 31);
            graphics.DrawEllipse(outline, 9, 5, 14, 22);
            graphics.DrawLine(line, 16, 7, 16, 13);
            graphics.FillEllipse(dot, 24, 22, 7, 7);
        }
        IntPtr handle = bitmap.GetHicon();
        try { using var source = Icon.FromHandle(handle); return (Icon)source.Clone(); }
        finally { DestroyIcon(handle); }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    private void Save(ClickerSettings value)
    {
        SettingsStore.Save(value);
        if (value != settings)
        {
            settings = value;
            service.Configure(value);
        }
        UpdateTray();
    }

    internal void ShowSettings()
    {
        service.Suspend(true);
        if (!form.Visible)
        {
            Point? position = windowPosition;
            form.LoadSettings(settings);
            if (position.HasValue && !form.IsHandleCreated) form.RestorePosition(position.Value);
            form.WindowState = FormWindowState.Normal;
            form.Show();
            // A hidden, previously minimized window can remain iconic at the native level.
            ShowWindow(form.Handle, 9); // SW_RESTORE
            if (position.HasValue) form.RestorePosition(position.Value);
            form.FitWorkingArea();
            TrackWindowPosition();
        }
        form.Activate();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    private void HideSettings()
    {
        RememberWindowPosition();
        form.Hide();
        service.Suspend(false);
        UpdateTray();
    }

    private void TrackWindowPosition()
    {
        // Windows may report its minimized sentinel before the form updates WindowState.
        if (form.Visible && form.WindowState == FormWindowState.Normal && form.Location is not { X: -32000, Y: -32000 })
            windowPosition = form.Location;
    }

    private void RememberWindowPosition()
    {
        TrackWindowPosition();
        if (windowPosition is { } position && savedWindowPosition != position && SettingsStore.TrySaveWindowPosition(position))
            savedWindowPosition = position;
    }

    private void UpdateTray()
    {
        var state = service.Status;
        enabledItem.Checked = state.Enabled;
        countItem.Text = $"Счётчики кликов: Л {state.LeftCount:N0} / П {state.RightCount:N0}";
        tray.Icon = state.Active ? activeIcon : !state.Enabled || state.Suspended ? pausedIcon : appIcon;
        string mode = settings.ClickMode == ClickMode.Automatic ? "автоклик" : settings.Mode == ActivationMode.Hold ? "удержание" : "переключатель";
        string stateText = state.Active ? "Кликает" : !state.Enabled ? "Отключён" : state.Suspended ? "Настройки" : state.Armed ? "Ждёт мышь" : "Готов";
        string text = $"{stateText} · {ClickerSettings.KeyName(settings.Hotkey)} · {mode}\nЛ {state.LeftCount:N0} / П {state.RightCount:N0} · Esc: стоп";
        tray.Text = text.Length > 63 ? text[..63] : text;
        if (state.Error != null && state.Error != previousError)
        {
            previousError = state.Error;
            if (!form.Visible) tray.ShowBalloonTip(5000, "Кликер остановлен", state.Error, ToolTipIcon.Warning);
        }
        if (state.Error == null) previousError = null;
    }

    protected override void ExitThreadCore()
    {
        if (exiting) return;
        exiting = true;
        timer.Stop();
        if (form.Visible) RememberWindowPosition();
        service.Dispose();
        mouse.Dispose();
        tray.Visible = false;
        form.Exiting = true;
        form.Close();
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (!exiting) ExitThreadCore();
            timer.Dispose(); tray.Dispose(); menu.Dispose(); form.Dispose();
            appIcon.Dispose(); activeIcon.Dispose(); pausedIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
