using App1.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace App1.Services;

public sealed class CommandStorageService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private static string StorageFilePath
    {
        get
        {
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "App1");

            return Path.Combine(root, "shell-commands.json");
        }
    }

    public async Task<IReadOnlyList<ShellCommandEntry>> LoadAsync()
    {
        if (!File.Exists(StorageFilePath))
        {
            return Array.Empty<ShellCommandEntry>();
        }

        await using FileStream stream = File.OpenRead(StorageFilePath);
        List<ShellCommandEntry>? commands = await JsonSerializer.DeserializeAsync<List<ShellCommandEntry>>(stream, SerializerOptions);
        return commands ?? new List<ShellCommandEntry>();
    }

    public async Task SaveAsync(IEnumerable<ShellCommandEntry> commands)
    {
        string? directory = Path.GetDirectoryName(StorageFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using FileStream stream = File.Create(StorageFilePath);
        await JsonSerializer.SerializeAsync(stream, commands.ToList(), SerializerOptions);
    }
}
