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
            UiSettingsModel? model = await LoadModelAsync();
            return string.IsNullOrWhiteSpace(model?.Language) ? null : model.Language;
        }

        public async Task SaveLanguageAsync(string language)
        {
            UiSettingsModel model = await LoadModelAsync() ?? new UiSettingsModel();
            model.Language = language;
            await SaveModelAsync(model);
        }

        public async Task<bool?> LoadBlurBackgroundEnabledAsync()
        {
            UiSettingsModel? model = await LoadModelAsync();
            return model?.BlurBackgroundEnabled;
        }

        public async Task SaveBlurBackgroundEnabledAsync(bool enabled)
        {
            UiSettingsModel model = await LoadModelAsync() ?? new UiSettingsModel();
            model.BlurBackgroundEnabled = enabled;
            await SaveModelAsync(model);
        }

        public async Task<string?> LoadThemeAsync()
        {
            UiSettingsModel? model = await LoadModelAsync();
            return string.IsNullOrWhiteSpace(model?.Theme) ? null : model.Theme;
        }

        public async Task SaveThemeAsync(string theme)
        {
            UiSettingsModel model = await LoadModelAsync() ?? new UiSettingsModel();
            model.Theme = theme;
            await SaveModelAsync(model);
        }

        private static async Task<UiSettingsModel?> LoadModelAsync()
        {
            if (!File.Exists(SettingsPath))
            {
                return null;
            }

            await using FileStream stream = File.OpenRead(SettingsPath);
            return await JsonSerializer.DeserializeAsync<UiSettingsModel>(stream);
        }

        private static async Task SaveModelAsync(UiSettingsModel model)
        {
            string? directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using FileStream stream = File.Create(SettingsPath);
            await JsonSerializer.SerializeAsync(stream, model, new JsonSerializerOptions { WriteIndented = true });
        }

        private sealed class UiSettingsModel
        {
            public string? Language { get; set; }
            public bool? BlurBackgroundEnabled { get; set; }
            public string? Theme { get; set; }
        }
    }
}
