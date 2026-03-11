using ShellCommandManager.Models;
using System.Collections.Generic;

namespace ShellCommandManager.Services;

public sealed class TemplateArgumentRenderService
{
    public string BuildStartupArguments(CommandTemplate template, IReadOnlyDictionary<string, object?> values)
    {
        List<string> parts = new();

        foreach (TemplateArgument argument in template.Arguments)
        {
            values.TryGetValue(argument.Key, out object? value);

            if (argument.Type == TemplateArgumentType.Bool)
            {
                bool enabled = value is bool boolValue && boolValue;
                if (enabled)
                {
                    parts.Add(argument.Key);
                }

                continue;
            }

            string textValue = value?.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(textValue))
            {
                continue;
            }

            if (argument.Type is TemplateArgumentType.File or TemplateArgumentType.Folder)
            {
                textValue = QuoteIfNeeded(textValue);
            }

            parts.Add(argument.Key);
            parts.Add(textValue);
        }

        return string.Join(" ", parts);
    }

    private static string QuoteIfNeeded(string value)
    {
        return value.Contains(' ') && !value.StartsWith('"') ? $"\"{value}\"" : value;
    }
}
