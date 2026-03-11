using ShellCommandManager.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ShellCommandManager.Services;

public sealed class TemplateStorageService
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private static string StorageFilePath
    {
        get
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShellCommandManager");
            return Path.Combine(root, "command-templates.json");
        }
    }

    public async Task<IReadOnlyList<CommandTemplate>> LoadAsync()
    {
        if (!File.Exists(StorageFilePath))
        {
            return Array.Empty<CommandTemplate>();
        }

        await using FileStream stream = File.OpenRead(StorageFilePath);
        List<CommandTemplate>? templates = await JsonSerializer.DeserializeAsync<List<CommandTemplate>>(stream, SerializerOptions);
        return templates ?? new List<CommandTemplate>();
    }

    public async Task SaveAsync(IEnumerable<CommandTemplate> templates)
    {
        string? directory = Path.GetDirectoryName(StorageFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using FileStream stream = File.Create(StorageFilePath);
        await JsonSerializer.SerializeAsync(stream, templates.ToList(), SerializerOptions);
    }
}
