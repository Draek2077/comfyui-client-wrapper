using System.IO;
using System.Text.Json;

namespace ComfyUIClientWrapper;

public class AppSettings
{
    public string Theme { get; set; } = "System Detected"; // Default theme
}

public static class SettingsManager
{
    private static readonly string AppDataFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ComfyUIClientWrapper");

    private static readonly string SettingsFilePath = Path.Combine(AppDataFolder, "settings.json");

    public static AppSettings LoadSettings()
    {
        if (!File.Exists(SettingsFilePath)) return new AppSettings(); // Return default settings if file doesn't exist

        try
        {
            var json = File.ReadAllText(SettingsFilePath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings(); // Return default on error
        }
    }

    public static void SaveSettings(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(AppDataFolder); // Ensure the directory exists
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, json);
        }
        catch (Exception ex)
        {
            // In a real app, you might want to log this error
            Console.WriteLine($"Error saving settings: {ex.Message}");
        }
    }
}