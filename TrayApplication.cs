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
    private string? previousError;
    private bool exiting;

    public TrayApplication()
    {
        settings = SettingsStore.Load(out string? warning);
        appIcon = CreateIcon(Color.FromArgb(59, 130, 246));
        activeIcon = CreateIcon(Color.FromArgb(34, 197, 94));
        pausedIcon = CreateIcon(Color.FromArgb(148, 163, 184));
        mouse = new PhysicalMouse();
        service = new(settings, mouse);
        form = new SettingsForm(settings, appIcon, () => service.Status, Save, HideSettings);
        menu = new ContextMenuStrip();
        menu.Items.Add("Настройки…", null, (_, _) => ShowSettings());
        enabledItem = new ToolStripMenuItem("Горячая клавиша включена") { Checked = true };
        enabledItem.Click += (_, _) => service.SetEnabled(!service.Status.Enabled);
        menu.Items.Add(enabledItem);
        menu.Items.Add("Остановить клики · Esc", null, (_, _) => service.Stop());
        menu.Items.Add(new ToolStripSeparator());
        countItem = new ToolStripMenuItem("Последний запуск: 0 / 0") { Enabled = false };
        menu.Items.Add(countItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => ExitThread());
        menu.Opening += (_, _) => service.Suspend(true);
        menu.Closed += (_, _) => { if (!exiting && !form.Visible) service.Suspend(false); };
        tray = new NotifyIcon { Icon = appIcon, Text = "Кликер · F6", ContextMenuStrip = menu, Visible = true };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowSettings(); };
        timer = new System.Windows.Forms.Timer { Interval = 150 };
        timer.Tick += (_, _) => UpdateTray();
        timer.Start();
        ShowSettings();
        if (warning != null) MessageBox.Show(form, warning, "Настройки", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        settings = value;
        service.Configure(value);
        UpdateTray();
    }

    private void ShowSettings()
    {
        service.Suspend(true);
        if (!form.Visible) { form.LoadSettings(settings); form.Show(); }
        form.WindowState = FormWindowState.Normal;
        if (!IsWindowVisible(form.Handle)) ShowWindow(form.Handle, 1);
        form.Activate();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    private void HideSettings()
    {
        form.Hide();
        service.Suspend(false);
        UpdateTray();
    }

    private void UpdateTray()
    {
        var state = service.Status;
        enabledItem.Checked = state.Enabled;
        countItem.Text = $"Клики за запуск: Л {state.LeftCount:N0} / П {state.RightCount:N0}";
        tray.Icon = state.Active ? activeIcon : !state.Enabled || state.Suspended ? pausedIcon : appIcon;
        string mode = settings.ClickMode == ClickMode.Automatic ? "автоклик" : settings.Mode == ActivationMode.Hold ? "удержание" : "переключатель";
        string stateText = state.Active ? "Кликает" : !state.Enabled ? "Отключён" : state.Suspended ? "Настройки" : state.Armed ? "Ждёт мышь" : "Готов";
        string text = $"{stateText} · {ClickerSettings.KeyName(settings.Hotkey)} · {mode}\nЛ {state.LeftCount:N0} / П {state.RightCount:N0} · Esc: стоп";
        tray.Text = text.Length > 63 ? text[..63] : text;
        if (state.Error != null && state.Error != previousError)
        {
            previousError = state.Error;
            tray.ShowBalloonTip(5000, "Кликер остановлен", state.Error, ToolTipIcon.Warning);
        }
        if (state.Error == null) previousError = null;
    }

    protected override void ExitThreadCore()
    {
        if (exiting) return;
        exiting = true;
        timer.Stop();
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
