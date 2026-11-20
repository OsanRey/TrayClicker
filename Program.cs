namespace TrayClicker;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception ex)
        {
            string log = Path.Combine(AppContext.BaseDirectory, "error.log");
            try { File.AppendAllText(log, DateTime.Now.ToString("s") + "\n" + ex + "\n\n"); } catch { }
            if (args.Length == 0) ReportError(ex);
            else Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Length > 0 && args[0] == "--self-test") return SelfTests.Run(args.ElementAtOrDefault(1));
        if (args.Length > 1 && args[0] == "--preview") return SelfTests.Preview(args[1]);
        if (args.Length > 1 && args[0] == "--smoke-test") return SelfTests.Smoke(args[1]);
        if (args.Length > 1 && args[0] == "--benchmark") return ClickBenchmark.Run(args[1]);
        if (args.Length > 1 && args[0] == "--benchmark-compare") return ClickBenchmark.Run(args[1], true);
        if (args.Length > 1 && args[0] == "--benchmark-worker") return ClickBenchmark.Worker(args[1]);

        using var mutex = new Mutex(true, "Local\\TrayClicker.Desktop.v1", out bool firstInstance);
        if (!firstInstance)
        {
            MessageBox.Show("Кликер уже запущен. Откройте его значок в системном трее рядом с часами.",
                "Кликер", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
        try
        {
            using var app = new TrayApplication();
            Application.Run(app);
            return 0;
        }
        finally { mutex.ReleaseMutex(); }
    }

    private static void ReportError(Exception ex) => MessageBox.Show(ex.Message,
        "Ошибка кликера", MessageBoxButtons.OK, MessageBoxIcon.Error);
}
