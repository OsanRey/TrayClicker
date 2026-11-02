namespace TrayClicker;

internal sealed class SettingsForm : Form, IMessageFilter
{
    private readonly Func<ClickerStatus> getStatus;
    private readonly Action<ClickerSettings> save;
    private readonly Action hideToTray;
    private readonly ComboBox clickMode = Choice(220);
    private readonly ComboBox keyMode = Choice(116);
    private readonly CheckBox leftEnabled = new() { Text = "Левая", AutoSize = true };
    private readonly CheckBox rightEnabled = new() { Text = "Правая", AutoSize = true };
    private readonly NumericUpDown leftRate = RateBox();
    private readonly NumericUpDown rightRate = RateBox();
    private readonly NumericUpDown limit = new() { Minimum = 1, Maximum = 1_000_000, ThousandsSeparator = true, Width = 100 };
    private readonly Button hotkeyButton = new() { Width = 92, Height = 28, FlatStyle = FlatStyle.Flat };
    private readonly Label hint = new() { Width = 288, Height = 31, ForeColor = Color.DimGray };
    private readonly Label status = new() { Height = 20, Width = 288, ForeColor = Color.FromArgb(45, 92, 143) };
    private readonly ToolTip tips = new();
    private readonly System.Windows.Forms.Timer refresh = new() { Interval = 250 };
    private bool capturing;
    private int capturedMouseButton;
    private int hotkey;
    private string? lastError;
    public bool Exiting { get; set; }

    public SettingsForm(ClickerSettings settings, Icon icon, Func<ClickerStatus> getStatus,
        Action<ClickerSettings> save, Action hideToTray)
    {
        this.getStatus = getStatus;
        this.save = save;
        this.hideToTray = hideToTray;
        Text = "Кликер";
        Icon = icon;
        Font = new Font("Segoe UI", 9f);
        BackColor = Color.FromArgb(248, 249, 251);
        ForeColor = Color.FromArgb(30, 41, 59);
        ClientSize = new Size(312, 278);
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        AddLabel("Режим", 12, 12, 66);
        clickMode.Items.AddRange(["Заданное число кликов", "Пока держу кнопку мыши", "Автоклик по активации"]);
        Place(clickMode, 80, 8);
        AddLabel("Кликов", 12, 44, 66);
        Place(limit, 80, 40);
        AddLabel("за нажатие", 188, 44, 112, Color.DimGray);
        AddLabel("Кнопка мыши", 12, 70, 170, Color.DimGray);
        AddLabel("Кликов/сек", 194, 70, 106, Color.DimGray);
        Place(leftEnabled, 12, 94);
        Place(leftRate, 194, 90);
        Place(rightEnabled, 12, 121);
        Place(rightRate, 194, 117);
        leftEnabled.CheckedChanged += (_, _) => leftRate.Enabled = leftEnabled.Checked;
        rightEnabled.CheckedChanged += (_, _) => rightRate.Enabled = rightEnabled.Checked;
        Place(new Panel { Width = 288, Height = 1, BackColor = Color.FromArgb(220, 225, 233) }, 12, 144);
        AddLabel("Клавиша", 12, 161, 66);
        Place(hotkeyButton, 80, 153);
        keyMode.Items.AddRange(["Удерживать", "Вкл / выкл"]);
        Place(keyMode, 184, 156);
        hotkeyButton.FlatAppearance.BorderColor = Color.FromArgb(184, 197, 212);
        hotkeyButton.Click += (_, _) => BeginCapture();
        tips.SetToolTip(hotkeyButton, "Нажмите и выберите клавишу или боковую кнопку мыши X1/X2. Esc — отменить выбор.");
        tips.SetToolTip(keyMode, "Удерживать: горячая клавиша должна быть зажата. Вкл / выкл: нажатие разрешает клики, повторное — запрещает.");
        tips.SetToolTip(limit, "Число кликов за серию. При активной горячей клавише нажатие мыши запускает серию соответствующей кнопки.");
        tips.SetToolTip(leftRate, "Скорость левой кнопки: 0,1–500 кликов в секунду.");
        tips.SetToolTip(rightRate, "Скорость правой кнопки: 0,1–500 кликов в секунду.");
        Place(hint, 12, 188);
        Place(status, 12, 222);
        clickMode.SelectedIndexChanged += (_, _) => UpdateMode();

        var saveButton = new Button { Text = "Сохранить", Width = 92, Height = 28, FlatStyle = FlatStyle.Flat };
        var trayButton = new Button { Text = "Сохранить и в трей", Width = 184, Height = 28,
            FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(40, 91, 165), ForeColor = Color.White };
        saveButton.FlatAppearance.BorderColor = Color.FromArgb(184, 197, 212);
        trayButton.FlatAppearance.BorderSize = 0;
        Place(saveButton, 12, 242);
        Place(trayButton, 116, 242);
        saveButton.Click += (_, _) => TrySave();
        trayButton.Click += (_, _) => { if (TrySave()) hideToTray(); };
        FormClosing += (_, e) =>
        {
            if (Exiting) return;
            e.Cancel = true;
            if (TrySave()) hideToTray();
        };
        Resize += (_, _) =>
        {
            if (WindowState != FormWindowState.Minimized) return;
            if (TrySave()) hideToTray();
            WindowState = FormWindowState.Normal;
        };
        KeyDown += OnKeyDown;
        Deactivate += (_, _) => EndCapture();
        VisibleChanged += (_, _) => { if (Visible) refresh.Start(); else { refresh.Stop(); EndCapture(); } };
        refresh.Tick += (_, _) => RefreshStatus();
        LoadSettings(settings);
        RefreshStatus();
        Application.AddMessageFilter(this);
    }

    private static ComboBox Choice(int width)
    {
        var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = width, DrawMode = DrawMode.OwnerDrawFixed };
        box.DrawItem += (_, e) =>
        {
            e.DrawBackground();
            if (e.Index >= 0)
            {
                var bounds = e.Bounds;
                bounds.X += 2;
                bounds.Width -= 4;
                TextRenderer.DrawText(e.Graphics, box.Items[e.Index]?.ToString(), box.Font, bounds,
                    e.ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                        | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
            e.DrawFocusRectangle();
        };
        return box;
    }
    private static NumericUpDown RateBox() => new()
    {
        Minimum = 0.1m, Maximum = 500, DecimalPlaces = 1, Increment = 1, Width = 106
    };
    private void Place(Control control, int x, int y)
    {
        control.Location = new Point(x, y);
        Controls.Add(control);
    }
    private void AddLabel(string text, int x, int y, int width, Color? color = null) =>
        Place(new Label { Text = text, Width = width, Height = 21, ForeColor = color ?? ForeColor }, x, y);

    public void LoadSettings(ClickerSettings settings)
    {
        hotkey = settings.Hotkey;
        keyMode.SelectedIndex = (int)settings.Mode;
        clickMode.SelectedIndex = (int)settings.ClickMode;
        leftEnabled.Checked = settings.LeftEnabled;
        rightEnabled.Checked = settings.RightEnabled;
        leftRate.Value = settings.LeftCps;
        rightRate.Value = settings.RightCps;
        leftRate.Enabled = settings.LeftEnabled;
        rightRate.Enabled = settings.RightEnabled;
        limit.Value = settings.ClickLimit;
        EndCapture();
    }
    private void UpdateMode()
    {
        limit.Enabled = clickMode.SelectedIndex == (int)ClickMode.Count;
        bool automatic = clickMode.SelectedIndex == (int)ClickMode.Automatic;
        keyMode.Enabled = !automatic;
        if (automatic) keyMode.SelectedIndex = (int)ActivationMode.Toggle;
        if (!capturing) hint.Text = automatic
            ? "Нажатие — запуск, повторное — стоп.\nУдерживать не нужно. Esc — остановить."
            : limit.Enabled
            ? "Клавиша + нажатие мыши → N кликов.\nEsc — остановить."
            : "Клавиша + удержание мыши → клики.\nОтпустили мышь — стоп. Esc — остановить.";
    }
    private void BeginCapture()
    {
        capturing = true;
        hotkeyButton.Text = "Нажмите…";
        hint.Text = "Нажмите клавишу или кнопку мыши X1/X2.\nEsc — отменить выбор.";
        hotkeyButton.Focus();
    }
    private void EndCapture()
    {
        capturing = false;
        hotkeyButton.Text = hotkey is (int)Keys.XButton1 or (int)Keys.XButton2
            ? ClickerSettings.KeyName(hotkey) : ClickerSettings.KeyName(hotkey) + " · сменить";
        UpdateMode();
    }
    public bool PreFilterMessage(ref Message message)
    {
        // Child controls receive mouse messages themselves, so filter on the UI thread.
        if (message.Msg is not (0x020B or 0x020C or 0x020D)) return false;
        int button = (int)((message.WParam.ToInt64() >> 16) & 0xFFFF);
        if (message.Msg == 0x020C && button == capturedMouseButton)
        {
            capturedMouseButton = 0;
            return true;
        }
        if (!capturing || !Visible || button is not (1 or 2)) return false;
        if (message.Msg == 0x020C) return false;
        capturedMouseButton = button;
        hotkey = button == 1 ? (int)Keys.XButton1 : (int)Keys.XButton2;
        EndCapture();
        return true;
    }
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!capturing) return;
        e.Handled = e.SuppressKeyPress = true;
        if (e.KeyCode == Keys.Escape) { EndCapture(); return; }
        if (!ClickerSettings.IsValidHotkey((int)e.KeyCode))
        {
            hint.Text = "Выберите клавишу или кнопку мыши X1/X2.\nCtrl, Alt, Shift и Win не подходят.";
            return;
        }
        hotkey = (int)e.KeyCode;
        EndCapture();
    }
    private bool TrySave()
    {
        EndCapture();
        if (!leftEnabled.Checked && !rightEnabled.Checked)
        {
            MessageBox.Show(this, "Выберите хотя бы одну кнопку мыши.", "Настройки", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }
        try
        {
            var value = new ClickerSettings
            {
                Hotkey = hotkey, Mode = (ActivationMode)keyMode.SelectedIndex,
                ClickMode = (ClickMode)clickMode.SelectedIndex,
                LeftEnabled = leftEnabled.Checked, RightEnabled = rightEnabled.Checked,
                LeftCps = leftRate.Value, RightCps = rightRate.Value, ClickLimit = (int)limit.Value
            }.Validated();
            save(value);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Не удалось сохранить настройки.\n" + ex.Message, "Настройки", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }
    private void RefreshStatus()
    {
        var state = getStatus();
        status.Text = state.Error != null ? "Ошибка клика. Включите через меню трея."
            : !state.Enabled ? "Горячая клавиша отключена в меню трея."
            : "В настройках клики приостановлены";
        if (state.Error != null && state.Error != lastError)
        {
            lastError = state.Error;
            MessageBox.Show(this, state.Error, "Кликер остановлен", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        if (state.Error == null) lastError = null;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { Application.RemoveMessageFilter(this); refresh.Dispose(); tips.Dispose(); }
        base.Dispose(disposing);
    }
}
