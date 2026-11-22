namespace TrayClicker;

internal sealed class SettingsForm : Form, IMessageFilter
{
    private static readonly Size LayoutSize = new(500, 383);
    private static readonly Color Canvas = Color.FromArgb(244, 246, 251);
    private static readonly Color Ink = Color.FromArgb(23, 32, 54);
    private static readonly Color Muted = Color.FromArgb(78, 92, 118);
    private static readonly Color Border = Color.FromArgb(212, 219, 233);
    private static readonly Color Accent = Color.FromArgb(61, 73, 197);
    private static readonly Color AccentWash = Color.FromArgb(237, 239, 255);
    private static readonly Color ErrorInk = Color.FromArgb(155, 39, 51);
    private static readonly Color ErrorWash = Color.FromArgb(255, 237, 239);

    private readonly Func<ClickerStatus> getStatus;
    private readonly Action<ClickerSettings> save;
    private readonly Action hideToTray;
    private readonly ComboBox clickMode = Choice("Режим кликов", 201, 0);
    private readonly ComboBox keyMode = Choice("Способ активации", 124, 1);
    private readonly CheckBox leftEnabled = new() { Text = "Левая кнопка", AutoSize = true, TabIndex = 0 };
    private readonly CheckBox rightEnabled = new() { Text = "Правая кнопка", AutoSize = true, TabIndex = 2 };
    private readonly NumericUpDown leftRate = RateBox("Скорость левой кнопки", 1);
    private readonly NumericUpDown rightRate = RateBox("Скорость правой кнопки", 3);
    private readonly NumericUpDown pressDuration = new()
    {
        Name = "PressDuration", AccessibleName = "Нажатие, мс", Maximum = 200, Increment = 5,
        Width = 82, TextAlign = HorizontalAlignment.Right, TabIndex = 4
    };
    private readonly NumericUpDown limit = new()
    {
        AccessibleName = "Кликов в серии", Minimum = 1, Maximum = 1_000_000,
        ThousandsSeparator = true, TextAlign = HorizontalAlignment.Right, Width = 82, TabIndex = 1
    };
    private readonly Button hotkeyButton = MakeButton("", 92, 0);
    private readonly Label limitLabel = Caption("Кликов в серии", 115);
    private readonly Label modeHelp = Caption("", 201, 32);
    private readonly Label keyHelp = Caption("", 215, 28);
    private readonly Label statusBadge = new()
    {
        Size = new(138, 27), TextAlign = ContentAlignment.MiddleCenter, AccessibleName = "Состояние кликера"
    };
    private readonly Label feedbackTitle = new() { Size = new(225, 20), AutoEllipsis = true };
    private readonly Label feedbackDetail = Caption("", 225, 20);
    private readonly ToolTip tips = new();
    private readonly System.Windows.Forms.Timer refresh = new() { Interval = 250 };
    private readonly Bitmap headerIcon;
    private ClickerSettings applied = new();
    private ActivationMode preferredKeyMode;
    private bool loading;
    private bool automaticMode;
    private bool capturing;
    private int capturedMouseButton;
    private int capturedKey;
    private int hotkey;
    private string? notice;
    private string? noticeDetail;
    private bool noticeIsError;
    private bool settingsNeedRepair;
    public bool Exiting { get; set; }

    public SettingsForm(ClickerSettings settings, Icon icon, Func<ClickerStatus> getStatus,
        Action<ClickerSettings> save, Action hideToTray, Action exit)
    {
        this.getStatus = getStatus;
        this.save = save;
        this.hideToTray = hideToTray;
        Text = "TrayClicker";
        Icon = icon;
        Font = new Font("Segoe UI", 9f);
        BackColor = Canvas;
        ForeColor = Ink;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = LayoutSize;
        AutoScroll = true;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;

        headerIcon = icon.ToBitmap();
        Place(this, new PictureBox { Image = headerIcon, Size = new(35, 35), SizeMode = PictureBoxSizeMode.Zoom }, 18, 15);
        Place(this, Heading("TrayClicker", 250, 33, 21), 62, 8);
        Place(this, Caption("Настройки автокликера", 250), 64, 40);
        Place(this, statusBadge, 344, 21);

        var modeCard = Card(0);
        Place(this, modeCard, 18, 69);
        Place(modeCard, Heading("Режим кликов", 201, 24, 12), 12, 10);
        clickMode.Name = "ClickMode";
        clickMode.Items.AddRange(["Заданное число кликов", "Пока держу кнопку мыши", "Автоклик по активации"]);
        Place(modeCard, clickMode, 12, 39);
        Place(modeCard, limitLabel, 12, 74);
        Place(modeCard, limit, 131, 70);
        Place(modeCard, modeHelp, 12, 100);

        var mouseCard = Card(1);
        Place(this, mouseCard, 257, 69);
        Place(mouseCard, Heading("Кнопки мыши", 201, 24, 12), 12, 10);
        Place(mouseCard, Caption("Скорость · кликов в секунду", 201), 12, 39);
        Place(mouseCard, leftEnabled, 12, 67);
        Place(mouseCard, leftRate, 131, 65);
        Place(mouseCard, rightEnabled, 12, 103);
        Place(mouseCard, rightRate, 131, 101);
        Place(mouseCard, Caption("Нажатие, мс", 115), 12, 136);
        Place(mouseCard, pressDuration, 131, 132);
        tips.SetToolTip(leftRate, "От 0,1 до 500 кликов в секунду.");
        tips.SetToolTip(rightRate, "От 0,1 до 500 кликов в секунду.");
        tips.SetToolTip(pressDuration, "0 — мгновенный клик. Для удержания и отпускания кнопки нужен интервал не короче длительности нажатия плюс 1 мс.");

        var activationCard = new CardPanel { Size = new(464, 64), TabIndex = 2 };
        Place(this, activationCard, 18, 249);
        Place(activationCard, Heading("Активация", 215, 24, 12), 12, 8);
        Place(activationCard, keyHelp, 12, 34);
        hotkeyButton.Name = "HotkeyButton";
        hotkeyButton.AutoEllipsis = true;
        hotkeyButton.BackColor = AccentWash;
        hotkeyButton.ForeColor = Accent;
        hotkeyButton.FlatAppearance.BorderColor = Color.FromArgb(191, 198, 242);
        Place(activationCard, hotkeyButton, 224, 15);
        keyMode.Name = "KeyMode";
        keyMode.Items.AddRange(["Удерживать", "Вкл / выкл"]);
        Place(activationCard, keyMode, 328, 20);
        tips.SetToolTip(limit, "От 1 до 1 000 000 кликов за одно нажатие мыши.");

        Place(this, feedbackTitle, 18, 326);
        Place(this, feedbackDetail, 18, 346);
        feedbackTitle.Font = new Font(Font, FontStyle.Bold);
        var exitButton = MakeButton("Выход", 76, 3);
        exitButton.Name = "ExitButton";
        var trayButton = MakeButton("Сохранить и в трей", 146, 4, primary: true);
        trayButton.Name = "TrayButton";
        Place(this, exitButton, 250, 327);
        Place(this, trayButton, 336, 327);

        clickMode.SelectedIndexChanged += (_, _) => { UpdateMode(); ClearNotice(); };
        keyMode.SelectedIndexChanged += (_, _) =>
        {
            if (!loading && !automaticMode) preferredKeyMode = (ActivationMode)keyMode.SelectedIndex;
            if (!loading && !automaticMode) ClearNotice();
        };
        leftEnabled.CheckedChanged += (_, _) => { leftRate.Enabled = leftEnabled.Checked; ClearNotice(); };
        rightEnabled.CheckedChanged += (_, _) => { rightRate.Enabled = rightEnabled.Checked; ClearNotice(); };
        foreach (var box in new[] { limit, leftRate, rightRate, pressDuration })
            box.ValueChanged += (_, _) => ClearNotice();
        hotkeyButton.Click += (_, _) => BeginCapture();
        exitButton.Click += (_, _) => exit();
        trayButton.Click += (_, _) => { if (TrySave(repairSettings: settingsNeedRepair)) hideToTray(); };
        FormClosing += (_, e) =>
        {
            if (Exiting || e.CloseReason == CloseReason.WindowsShutDown) return;
            e.Cancel = true;
            if (TrySave()) hideToTray();
        };
        Resize += (_, _) =>
        {
            if (WindowState != FormWindowState.Minimized) return;
            if (TrySave()) hideToTray();
            WindowState = FormWindowState.Normal;
        };
        Deactivate += (_, _) =>
        {
            capturedKey = capturedMouseButton = 0;
            if (!Disposing) EndCapture();
        };
        VisibleChanged += (_, _) =>
        {
            if (Disposing) return;
            if (Visible) { RefreshStatus(); refresh.Start(); }
            else { refresh.Stop(); EndCapture(); }
        };
        Shown += (_, _) => FitWorkingArea();
        DpiChanged += (_, _) => BeginInvoke(FitWorkingArea);
        refresh.Tick += (_, _) => RefreshStatus();
        LoadSettings(settings);
        Application.AddMessageFilter(this);
    }

    private static Panel Card(int tabIndex) => new CardPanel { Size = new(225, 168), TabIndex = tabIndex };

    internal void RestorePosition(Point position)
    {
        StartPosition = FormStartPosition.Manual;
        Location = ClampPosition(position, Size, Screen.FromPoint(position).WorkingArea);
    }

    internal void FitWorkingArea()
    {
        if (IsDisposed || Disposing || !Visible || WindowState != FormWindowState.Normal) return;
        var area = Screen.FromControl(this).WorkingArea;
        if (area.Width <= 0 || area.Height <= 0) return;
        var scaled = new Size(LogicalToDeviceUnits(LayoutSize.Width), LogicalToDeviceUnits(LayoutSize.Height));
        var ideal = SizeFromClientSize(scaled);
        Size = new Size(Math.Min(ideal.Width, area.Width), Math.Min(ideal.Height, area.Height));
        Location = ClampPosition(Location, Size, area);
    }

    private static Point ClampPosition(Point position, Size size, Rectangle area) =>
        area.IsEmpty ? position : new Point(
            Math.Clamp(position.X, area.Left, Math.Max(area.Left, area.Right - size.Width)),
            Math.Clamp(position.Y, area.Top, Math.Max(area.Top, area.Bottom - size.Height)));

    private sealed class CardPanel : Panel
    {
        public CardPanel() => BackColor = Color.White;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var pen = new Pen(Border);
            e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }
    }

    private Label Heading(string text, int width, int height, float fontSize) => new()
    {
        Text = text, Size = new(width, height), Font = new Font(Font.FontFamily, fontSize, FontStyle.Bold)
    };

    private static Label Caption(string text, int width, int height = 22) => new()
    {
        Text = text, Size = new(width, height), ForeColor = Muted
    };

    private static ComboBox Choice(string name, int width, int tabIndex) => new()
    {
        AccessibleName = name, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
        BackColor = Canvas, ForeColor = Ink, Width = width, TabIndex = tabIndex
    };

    private static NumericUpDown RateBox(string name, int tabIndex) => new()
    {
        AccessibleName = name, Minimum = 0.1m, Maximum = 500, DecimalPlaces = 1,
        Increment = 1, Width = 82, TextAlign = HorizontalAlignment.Right, TabIndex = tabIndex
    };

    private static Button MakeButton(string text, int width, int tabIndex, bool primary = false)
    {
        var button = new Button
        {
            Text = text, Size = new(width, 33), TabIndex = tabIndex, FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Ink,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderColor = primary ? Accent : Border;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(49, 60, 174) : AccentWash;
        button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(41, 49, 145) : Border;
        return button;
    }

    private static void Place(Control parent, Control control, int x, int y)
    {
        control.Location = new Point(x, y);
        parent.Controls.Add(control);
    }

    public void LoadSettings(ClickerSettings settings)
    {
        loading = true;
        applied = settings.Validated();
        hotkey = applied.Hotkey;
        preferredKeyMode = applied.Mode;
        clickMode.SelectedIndex = (int)applied.ClickMode;
        leftEnabled.Checked = applied.LeftEnabled;
        rightEnabled.Checked = applied.RightEnabled;
        leftRate.Value = applied.LeftCps;
        rightRate.Value = applied.RightCps;
        pressDuration.Value = applied.PressMilliseconds;
        leftRate.Enabled = applied.LeftEnabled;
        rightRate.Enabled = applied.RightEnabled;
        limit.Value = applied.ClickLimit;
        EndCapture();
        loading = false;
        ClearNotice();
    }

    private void UpdateMode()
    {
        var mode = (ClickMode)clickMode.SelectedIndex;
        limit.Visible = limitLabel.Visible = mode == ClickMode.Count;
        modeHelp.Top = mode == ClickMode.Count ? LogicalToDeviceUnits(100) : LogicalToDeviceUnits(73);
        automaticMode = mode == ClickMode.Automatic;
        // Automatic uses toggle at runtime but keeps the preference for the other modes.
        bool wasLoading = loading;
        loading = true;
        keyMode.SelectedIndex = (int)(automaticMode ? ActivationMode.Toggle : preferredKeyMode);
        keyMode.Enabled = !automaticMode;
        loading = wasLoading;
        modeHelp.Text = mode switch
        {
            ClickMode.Count => "Щелчок запускает серию,\nесли активна горячая клавиша.",
            ClickMode.MouseHold => "Клики идут, пока зажата мышь\nи активна горячая клавиша.",
            _ => "Первое нажатие — запуск.\nПовторное — остановка."
        };
    }

    private void BeginCapture()
    {
        capturing = true;
        hotkeyButton.Text = "Нажмите…";
        keyHelp.Text = "Клавиша или X1/X2. Esc — отмена.";
        hotkeyButton.Focus();
    }

    private void EndCapture()
    {
        capturing = false;
        hotkeyButton.Text = ClickerSettings.KeyName(hotkey);
        hotkeyButton.AccessibleName = $"Сменить горячую клавишу. Выбрано: {hotkeyButton.Text}";
        tips.SetToolTip(hotkeyButton, $"Сменить горячую клавишу · {hotkeyButton.Text}");
        keyHelp.Text = "Esc останавливает клики.";
        UpdateMode();
    }

    public bool PreFilterMessage(ref Message message)
    {
        if (!Visible || Control.FromChildHandle(message.HWnd)?.FindForm() != this) return false;
        if (message.Msg is 0x0100 or 0x0101 or 0x0104 or 0x0105)
            return FilterKey(message.Msg, message.WParam.ToInt32(), (message.LParam.ToInt64() & (1L << 30)) != 0);

        if (message.Msg is not (0x020B or 0x020C or 0x020D)) return false;
        int button = (int)((message.WParam.ToInt64() >> 16) & 0xFFFF);
        if (message.Msg == 0x020C && button == capturedMouseButton)
        {
            capturedMouseButton = 0;
            return true;
        }
        if (!capturing || message.Msg == 0x020C || button is not (1 or 2)) return false;
        capturedMouseButton = button;
        hotkey = button == 1 ? (int)Keys.XButton1 : (int)Keys.XButton2;
        EndCapture();
        ClearNotice();
        return true;
    }

    private bool FilterKey(int message, int key, bool repeat)
    {
        bool up = message is 0x0101 or 0x0105;
        if (capturedKey != 0 && key == capturedKey)
        {
            if (up) capturedKey = 0;
            return true;
        }
        if (!capturing || up) return false;
        if (repeat) return true;
        if (key == (int)Keys.Escape)
        {
            capturedKey = key;
            EndCapture();
            return true;
        }
        if (!ClickerSettings.IsValidHotkey(key))
        {
            keyHelp.Text = "Ctrl, Alt, Shift и Win недоступны по отдельности.";
            return true;
        }
        capturedKey = hotkey = key;
        EndCapture();
        ClearNotice();
        return true;
    }

    private bool TrySave(bool repairSettings = false)
    {
        EndCapture();
        if (!leftEnabled.Checked && !rightEnabled.Checked)
        {
            SetNotice("Выберите кнопку мыши", "Нужна хотя бы одна кнопка.", isError: true);
            leftEnabled.Focus();
            return false;
        }

        var value = new ClickerSettings
        {
            Hotkey = hotkey, Mode = preferredKeyMode, ClickMode = (ClickMode)clickMode.SelectedIndex,
            LeftEnabled = leftEnabled.Checked, RightEnabled = rightEnabled.Checked,
            LeftCps = leftRate.Value, RightCps = rightRate.Value, ClickLimit = (int)limit.Value,
            PressMilliseconds = (int)pressDuration.Value
        };
        if (!repairSettings && value == applied) return true;

        try
        {
            save(value);
            applied = value;
            settingsNeedRepair = false;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetNotice("Не удалось сохранить настройки", ex.Message, isError: true);
            return false;
        }
    }

    public void ShowLoadWarning(string warning)
    {
        settingsNeedRepair = true;
        SetNotice("Загружены стандартные настройки", warning, isError: true);
    }

    private void SetNotice(string title, string detail, bool isError = false)
    {
        notice = title;
        noticeDetail = detail;
        noticeIsError = isError;
        RefreshStatus();
    }

    private void ClearNotice()
    {
        if (loading || notice == null) return;
        notice = noticeDetail = null;
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        var state = getStatus();
        bool hasError = state.Error != null;
        statusBadge.Text = hasError ? "Ошибка кликера" : !state.Enabled ? "Кликер отключён" : "Клики на паузе";
        statusBadge.BackColor = hasError ? ErrorWash : state.Enabled ? AccentWash : Color.FromArgb(231, 235, 243);
        statusBadge.ForeColor = hasError ? ErrorInk : state.Enabled ? Accent : Muted;
        string title = notice ?? (hasError ? "Кликер остановлен" : "Счётчики кликов");
        string detail = noticeDetail ?? state.Error ?? $"Левая: {state.LeftCount:N0}   ·   Правая: {state.RightCount:N0}";
        if (feedbackTitle.Text != title)
        {
            feedbackTitle.Text = title;
            tips.SetToolTip(feedbackTitle, title);
        }
        feedbackTitle.ForeColor = (notice == null ? hasError : noticeIsError) ? ErrorInk : Ink;
        if (feedbackDetail.Text != detail)
        {
            feedbackDetail.Text = detail;
            feedbackDetail.AutoEllipsis = true;
            tips.SetToolTip(feedbackDetail, detail);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Application.RemoveMessageFilter(this);
            refresh.Dispose();
            tips.Dispose();
            headerIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
