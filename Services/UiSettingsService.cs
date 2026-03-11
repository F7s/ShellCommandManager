using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace ShellCommandManager.Services
{
    public sealed class UiSettingsService
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShellCommandManager",
            "ui-settings.json");

        public async Task<string?> LoadLanguageAsync()
        {
            if (!File.Exists(SettingsPath))
            {
                return null;
            }

            await using FileStream stream = File.OpenRead(SettingsPath);
            UiSettingsModel? model = await JsonSerializer.DeserializeAsync<UiSettingsModel>(stream);
            return string.IsNullOrWhiteSpace(model?.Language) ? null : model.Language;
        }

        public async Task SaveLanguageAsync(string language)
        {
            string? directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            UiSettingsModel model = new() { Language = language };
            await using FileStream stream = File.Create(SettingsPath);
            await JsonSerializer.SerializeAsync(stream, model, new JsonSerializerOptions { WriteIndented = true });
        }

        private sealed class UiSettingsModel
        {
            public string? Language { get; set; }
        }
    }
}
