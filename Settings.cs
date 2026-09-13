using System.Text.Json;

namespace ShowTextOnly;

/// <summary>
/// The user's preferences, kept in ShowTextOnly.settings.json next to the executable.
/// </summary>
sealed class Settings
{
    static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "ShowTextOnly.settings.json");

    public string FontFamily { get; set; } = "Consolas";
    public float FontSize { get; set; } = 14f;
    public bool FontBold { get; set; }
    public bool FontItalic { get; set; }
    public bool IsDarkTheme { get; set; }
    public bool TopMost { get; set; } = true;
    public bool ShowInTaskbar { get; set; }
    public bool UseLfLineEndings { get; set; }
    public double Opacity { get; set; } = 1.0;
    public int? CustomForeground { get; set; }
    public int? CustomBackground { get; set; }
    public int WindowX { get; set; } = -1;
    public int WindowY { get; set; } = -1;
    public int WindowWidth { get; set; } = 420;
    public int WindowHeight { get; set; } = 260;

    /// <summary>
    /// Reads the settings file, or returns defaults when it is missing or cannot be parsed.
    /// </summary>
    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch (Exception exception)
        {
            ErrorLog.Write("Failed to load settings", exception);
        }
        return new Settings();
    }

    /// <summary>
    /// Writes the settings file.
    /// </summary>
    public void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch (Exception exception)
        {
            ErrorLog.Write("Failed to save settings", exception);
        }
    }
}
