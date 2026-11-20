using System.Text.Json;

namespace TrayClicker;

internal enum ActivationMode { Hold, Toggle }
internal enum ClickMode { Count, MouseHold, Automatic }
internal enum MouseButton { Left, Right }

internal sealed record ClickerSettings
{
    public int Hotkey { get; init; } = (int)Keys.F6;
    public ActivationMode Mode { get; init; } = ActivationMode.Hold;
    public ClickMode ClickMode { get; init; } = ClickMode.Count;
    public bool LeftEnabled { get; init; } = true;
    public bool RightEnabled { get; init; }
    public decimal LeftCps { get; init; } = 10;
    public decimal RightCps { get; init; } = 10;
    public int ClickLimit { get; init; } = 20;

    public static bool IsValidHotkey(int key) => key is > 0 and < 255
        && key is not ((int)Keys.Escape) and not ((int)Keys.LWin) and not ((int)Keys.RWin)
        && key is not ((int)Keys.ShiftKey) and not ((int)Keys.ControlKey) and not ((int)Keys.Menu)
        && key is not ((int)Keys.LShiftKey) and not ((int)Keys.RShiftKey)
        && key is not ((int)Keys.LControlKey) and not ((int)Keys.RControlKey)
        && key is not ((int)Keys.LMenu) and not ((int)Keys.RMenu)
        && key is not ((int)Keys.LButton) and not ((int)Keys.RButton) and not ((int)Keys.MButton);

    public ClickerSettings Validated() => this with
    {
        Hotkey = IsValidHotkey(Hotkey) ? Hotkey : (int)Keys.F6,
        Mode = Enum.IsDefined(Mode) ? Mode : ActivationMode.Hold,
        ClickMode = Enum.IsDefined(ClickMode) ? ClickMode : ClickMode.Count,
        LeftCps = Math.Clamp(LeftCps, 0.1m, 500m),
        RightCps = Math.Clamp(RightCps, 0.1m, 500m),
        ClickLimit = Math.Clamp(ClickLimit, 1, 1_000_000),
        LeftEnabled = LeftEnabled || !RightEnabled
    };

    public static string KeyName(int key) => key switch
    {
        (int)Keys.XButton1 => "Мышь X1",
        (int)Keys.XButton2 => "Мышь X2",
        (int)Keys.Oemtilde => "~ / Ё",
        (int)Keys.Space => "Пробел",
        (int)Keys.Return => "Enter",
        (int)Keys.Back => "Backspace",
        _ => new KeysConverter().ConvertToString((Keys)key) ?? $"VK {key}"
    };
}

internal static class SettingsStore
{
    // Portable: moving the folder also moves the user's preferences.
    public static readonly string DirectoryPath = AppContext.BaseDirectory;
    public static readonly string FilePath = Path.Combine(DirectoryPath, "settings.json");
    private static readonly string WindowPositionPath = Path.Combine(DirectoryPath, "window-position.json");

    private sealed record WindowPosition
    {
        public required int X { get; init; }
        public required int Y { get; init; }
    }

    public static ClickerSettings Load(out string? warning)
    {
        warning = null;
        try
        {
            var settings = JsonSerializer.Deserialize<ClickerSettings>(File.ReadAllText(FilePath));
            return settings?.Validated() ?? throw new JsonException("Файл настроек должен содержать объект JSON.");
        }
        catch (FileNotFoundException) { return new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            warning = ex.Message;
            return new();
        }
    }

    public static void Save(ClickerSettings settings) => WriteJson(FilePath, settings);

    public static Point? LoadWindowPosition()
    {
        try
        {
            var position = JsonSerializer.Deserialize<WindowPosition>(File.ReadAllText(WindowPositionPath));
            return position == null ? null : new Point(position.X, position.Y);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static bool TrySaveWindowPosition(Point position)
    {
        try
        {
            WriteJson(WindowPositionPath, new WindowPosition { X = position.X, Y = position.Y });
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(DirectoryPath);
        string temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temp); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
