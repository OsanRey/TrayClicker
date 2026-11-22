using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace TrayClicker;

// Runs the real scheduler, low-level hook and SendInput in a separate, invisible process.
// The visible receiving window counts Windows button-up messages, not a mocked click callback.
internal static class ClickBenchmark
{
    private sealed record Request(long Window, double Seconds, bool Legacy, decimal Left, decimal Right, int PressMilliseconds, string ResultPath);
    private sealed record Result(decimal LeftTarget, decimal RightTarget, bool Legacy, long SentLeft, long SentRight,
        double Seconds, string? Error);
    private sealed class Receiver : Form
    {
        public long LeftCount, RightCount;
        public string Progress = "Подготовка…";
        public Receiver()
        {
            Text = "Кликер — измерение реальной скорости";
            ClientSize = new Size(470, 180);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            TopMost = true;
            KeyPreview = true;
            BackColor = Color.FromArgb(248, 249, 251);
            Font = new Font("Segoe UI", 10);
            DoubleBuffered = true;
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0202) LeftCount++;
            if (m.Msg == 0x0205) RightCount++;
            base.WndProc(ref m);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            TextRenderer.DrawText(e.Graphics, "Проверка таймера и реальных кликов", Font, new Point(16, 18), Color.FromArgb(30, 41, 59));
            TextRenderer.DrawText(e.Graphics, Progress, Font, new Point(16, 52), Color.FromArgb(40, 91, 165));
            TextRenderer.DrawText(e.Graphics, $"Получено: Л {LeftCount:N0} / П {RightCount:N0}", Font, new Point(16, 86), ForeColor);
            TextRenderer.DrawText(e.Graphics, "Esc или выход курсора из окна — отменить тест.", Font, new Point(16, 132), Color.DimGray);
        }
    }

    public static int Run(string reportPath, bool compare = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        using var form = new Receiver();
        using var cancel = new CancellationTokenSource();
        using var refresh = new System.Windows.Forms.Timer { Interval = 200 };
        refresh.Tick += (_, _) => form.Invalidate();
        form.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) cancel.Cancel(); };
        form.FormClosing += (_, _) => cancel.Cancel();
        int exitCode = 1;
        form.Shown += async (_, _) =>
        {
            var report = new StringBuilder();
            Point originalCursor = Cursor.Position;
            IntPtr originalForeground = GetForegroundWindow();
            Point target = form.PointToScreen(new Point(form.ClientSize.Width / 2, form.ClientSize.Height / 2));
            Process? child = null;
            try
            {
                await Task.Delay(200, cancel.Token); // Let the initial Show/Shown sequence finish before placing the receiver.
                ShowWindow(form.Handle, 1);
                SetWindowPos(form.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0043);
                form.Activate();
                form.BringToFront();
                SetForegroundWindow(form.Handle);
                Cursor.Position = target;
                report.AppendLine($"Receiver visible={IsWindowVisible(form.Handle)}; bounds={form.Bounds}; target={target}; cursor={Cursor.Position}.");
                GetCursorPos(out var placed);
                IntPtr root = GetAncestor(WindowFromPoint(placed), 2);
                GetWindowThreadProcessId(root, out uint rootProcess);
                var windowClass = new StringBuilder(128);
                GetClassName(root, windowClass, windowClass.Capacity);
                report.AppendLine($"Receiver handle={form.Handle}, ownRoot={GetAncestor(form.Handle, 2)}, PID={Environment.ProcessId}; hit={root}, PID={rootProcess}, class={windowClass}.");
                if (root != form.Handle) throw new InvalidOperationException("Тестовое окно перекрыто; клики не запущены.");
                refresh.Start();
                var trials = new List<(bool legacy, decimal left, decimal right, int pressMilliseconds, double seconds)>
                {
                    (false, 100, 0, 5, 15)
                };
                foreach (bool legacy in compare ? new[] { true, false } : new[] { false })
                    foreach (decimal rate in new[] { 52m, 202m, 500m }) trials.Add((legacy, rate, 0, 0, 2));
                trials.Add((false, 0, 202, 0, 2));
                trials.Add((false, 202, 500, 0, 2));
                int trial = 0;
                foreach (var spec in trials)
                {
                    cancel.Token.ThrowIfCancellationRequested();
                    GetCursorPos(out var currentPoint);
                    if (GetAncestor(WindowFromPoint(currentPoint), 2) != form.Handle)
                        throw new OperationCanceledException("Курсор вышел из тестового окна; тест остановлен.");
                    form.LeftCount = form.RightCount = 0;
                    form.Progress = $"{++trial}/{trials.Count}: Л {spec.left} / П {spec.right} CPS · нажатие {spec.pressMilliseconds} мс";
                    form.Invalidate();
                    string requestPath = Path.Combine(Path.GetDirectoryName(reportPath)!, $"benchmark-{trial}-request.json");
                    string resultPath = Path.Combine(Path.GetDirectoryName(reportPath)!, $"benchmark-{trial}-result.json");
                    var request = new Request(form.Handle.ToInt64(), spec.seconds, spec.legacy, spec.left, spec.right, spec.pressMilliseconds, resultPath);
                    File.WriteAllText(requestPath, JsonSerializer.Serialize(request));
                    var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
                    start.ArgumentList.Add("--benchmark-worker"); start.ArgumentList.Add(requestPath);
                    child = Process.Start(start) ?? throw new IOException("Не удалось запустить измерительный процесс.");
                    await child.WaitForExitAsync(cancel.Token);
                    await Task.Delay(80, cancel.Token); // Drain already queued receiver input messages.
                    if (!File.Exists(resultPath)) throw new IOException("Измерительный процесс не оставил отчёт.");
                    var result = JsonSerializer.Deserialize<Result>(File.ReadAllText(resultPath))!;
                    double left = form.LeftCount / result.Seconds;
                    double right = form.RightCount / result.Seconds;
                    string line = $"{(spec.legacy ? "LEGACY" : "PRECISE")}: target L={spec.left}, R={spec.right}; press={spec.pressMilliseconds}ms; "
                        + $"received L={form.LeftCount}, R={form.RightCount}; elapsed={result.Seconds:F4}s; "
                        + $"CPS L={left:F2}, R={right:F2}; sent L={result.SentLeft}, R={result.SentRight}";
                    report.AppendLine(line);
                    File.WriteAllText(reportPath, report.ToString());
                    if (result.Error != null) throw new InvalidOperationException(result.Error);
                    if (form.LeftCount != result.SentLeft || form.RightCount != result.SentRight)
                        throw new InvalidOperationException("Не все отправленные клики получены тестовым окном.");
                    if (!spec.legacy && ((spec.left > 0 && Math.Abs(left / (double)spec.left - 1) > .03)
                        || (spec.right > 0 && Math.Abs(right / (double)spec.right - 1) > .03)))
                        throw new InvalidOperationException("Фактическая скорость отличается от заданной больше чем на 3%.");
                    child.Dispose(); child = null;
                }
                report.AppendLine("PASS: all injected clicks received; precise rates within 3%; both mouse buttons tested.");
                exitCode = 0;
            }
            catch (Exception ex) { report.AppendLine("FAIL: " + ex.Message); }
            finally
            {
                if (child != null) { if (!child.HasExited) child.Kill(); child.Dispose(); }
                refresh.Stop();
                File.WriteAllText(reportPath, report.ToString());
                if (Cursor.Position == target) Cursor.Position = originalCursor;
                if (GetForegroundWindow() == form.Handle && originalForeground != IntPtr.Zero) SetForegroundWindow(originalForeground);
                form.Close();
            }
        };
        Application.Run(form);
        return exitCode;
    }

    public static int Worker(string requestPath)
    {
        var request = JsonSerializer.Deserialize<Request>(File.ReadAllText(requestPath))!;
        IntPtr window = new(request.Window);
        using var hiddenWindow = new Form();
        _ = hiddenWindow.Handle; // Reproduce the window-owning, invisible tray process.
        using var mouse = new PhysicalMouse();
        int running = 0;
        long firstTimestamp = 0;
        long sentLeft = 0, sentRight = 0;
        var settings = new ClickerSettings { ClickMode = ClickMode.MouseHold,
            PressMilliseconds = request.PressMilliseconds,
            LeftEnabled = request.Left > 0, RightEnabled = request.Right > 0,
            LeftCps = request.Left > 0 ? request.Left : 10, RightCps = request.Right > 0 ? request.Right : 10 };
        void CheckTarget()
        {
            bool cursorRead = GetCursorPos(out var point);
            IntPtr hit = GetAncestor(WindowFromPoint(point), 2);
            if (!cursorRead || hit != window || !IsWindowVisible(window))
                throw new OperationCanceledException($"Курсор вышел из тестового окна; отправка кликов остановлена. Read={cursorRead}, visible={IsWindowVisible(window)}, hit={hit}, target={window}, point={point.X},{point.Y}.");
        }
        void CountSend(MouseButton button)
        {
            if (firstTimestamp == 0) firstTimestamp = Stopwatch.GetTimestamp();
            if (button == MouseButton.Left) sentLeft++; else sentRight++;
        }
        using var service = new ClickerService(settings, mouse,
            _ => new(Volatile.Read(ref running) == 1, NativeInput.IsDown((int)Keys.Escape),
                request.Left > 0 && Volatile.Read(ref running) == 1,
                request.Right > 0 && Volatile.Read(ref running) == 1, 0, 0),
            button =>
            {
                CheckTarget();
                NativeInput.Click(button);
                CountSend(button);
            }, request.Legacy,
            (button, down) =>
            {
                if (down) CheckTarget();
                NativeInput.SetButton(button, down);
                if (down) CountSend(button);
            });
        service.Suspend(false);
        Thread.Sleep(80); // Ensure the hotkey-release state was observed before arming.
        Volatile.Write(ref running, 1);
        Thread.Sleep(TimeSpan.FromSeconds(request.Seconds));
        service.Stop();
        long stop = Stopwatch.GetTimestamp();
        double seconds = firstTimestamp == 0 ? request.Seconds : (stop - firstTimestamp) / (double)Stopwatch.Frequency;
        var result = new Result(request.Left, request.Right, request.Legacy, sentLeft, sentRight, seconds, service.Status.Error);
        File.WriteAllText(request.ResultPath, JsonSerializer.Serialize(result));
        return result.Error == null ? 0 : 1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder value, int length);
}
