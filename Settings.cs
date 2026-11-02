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

    public static ClickerSettings Load(out string? warning)
    {
        warning = null;
        try
        {
            if (!File.Exists(FilePath)) return new();
            return (JsonSerializer.Deserialize<ClickerSettings>(File.ReadAllText(FilePath)) ?? new()).Validated();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            warning = "Не удалось прочитать настройки. Загружены стандартные значения.\n" + ex.Message;
            return new();
        }
    }

    public static void Save(ClickerSettings settings)
    {
        Directory.CreateDirectory(DirectoryPath);
        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, FilePath, true);
    }
}
