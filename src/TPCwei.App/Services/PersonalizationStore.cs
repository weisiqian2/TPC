using System.Text.Encodings.Web;
using System.Text.Json;
using TPC.App.Models;

namespace TPC.App.Services;

public sealed class PersonalizationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    public string BaseDirectory { get; }
    public string SettingsPath => Path.Combine(BaseDirectory, "personalization.json");
    public string? LastLoadError { get; private set; }

    public PersonalizationStore(string? baseDirectory = null)
    {
        BaseDirectory = baseDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TPCwei");
        Directory.CreateDirectory(BaseDirectory);
    }

    public AppPersonalizationSettings Load()
    {
        LastLoadError = null;
        if (!File.Exists(SettingsPath))
        {
            return AppPersonalizationSettings.CreateDefault();
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppPersonalizationSettings>(json, JsonOptions)
                ?? AppPersonalizationSettings.CreateDefault();
            settings.Normalize();
            return settings;
        }
        catch (Exception ex)
        {
            LastLoadError = ex.Message;
            return AppPersonalizationSettings.CreateDefault();
        }
    }

    public async Task SaveAsync(AppPersonalizationSettings settings, CancellationToken cancellationToken = default)
    {
        settings.Normalize();
        Directory.CreateDirectory(BaseDirectory);
        var tempPath = SettingsPath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }

        if (File.Exists(SettingsPath))
        {
            File.Replace(tempPath, SettingsPath, null);
        }
        else
        {
            File.Move(tempPath, SettingsPath);
        }
    }
}
