using System.Text.Json;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System;

namespace App1.Services;

public sealed class RuntimeValueHistoryService
{
    private const int MaxRecentValues = 10;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private static string StorageFilePath
    {
        get
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "App1");
            return Path.Combine(root, "runtime-value-history.json");
        }
    }

    private readonly Dictionary<string, List<string>> _history = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    public async Task<IReadOnlyList<string>> GetRecentValuesAsync(string commandKey, string argumentKey)
    {
        await EnsureLoadedAsync();
        string key = BuildStorageKey(commandKey, argumentKey);
        return _history.TryGetValue(key, out List<string>? values) ? values : Array.Empty<string>();
    }

    public async Task SaveRecentValueAsync(string commandKey, string argumentKey, string value)
    {
        string normalized = value.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        await EnsureLoadedAsync();
        string key = BuildStorageKey(commandKey, argumentKey);
        if (!_history.TryGetValue(key, out List<string>? values))
        {
            values = new List<string>();
            _history[key] = values;
        }

        values.RemoveAll(v => string.Equals(v, normalized, StringComparison.OrdinalIgnoreCase));
        values.Insert(0, normalized);
        if (values.Count > MaxRecentValues)
        {
            values.RemoveRange(MaxRecentValues, values.Count - MaxRecentValues);
        }

        await PersistAsync();
    }

    private async Task EnsureLoadedAsync()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;

        if (!File.Exists(StorageFilePath))
        {
            return;
        }

        await using FileStream stream = File.OpenRead(StorageFilePath);
        Dictionary<string, List<string>>? loaded = await JsonSerializer.DeserializeAsync<Dictionary<string, List<string>>>(stream, SerializerOptions);
        if (loaded is null)
        {
            return;
        }

        _history.Clear();
        foreach ((string key, List<string> values) in loaded)
        {
            _history[key] = values;
        }
    }

    private async Task PersistAsync()
    {
        string? directory = Path.GetDirectoryName(StorageFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using FileStream stream = File.Create(StorageFilePath);
        await JsonSerializer.SerializeAsync(stream, _history, SerializerOptions);
    }

    private static string BuildStorageKey(string commandKey, string argumentKey)
    {
        return $"{commandKey}::{argumentKey}";
    }
}
