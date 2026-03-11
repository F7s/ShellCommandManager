using ShellCommandManager.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ShellCommandManager.Services;

public sealed class TemplateImportService
{
    private readonly IDeserializer _yamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<CommandTemplate> ImportFromFileAsync(string filePath)
    {
        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        string content = await File.ReadAllTextAsync(filePath);

        CommandTemplateRaw templateRaw = extension switch
        {
            ".json" => ParseJson(content),
            ".yaml" or ".yml" => ParseYaml(content),
            _ => throw new InvalidOperationException("仅支持 JSON 或 YAML 模板文件。")
        };

        CommandTemplate template = MapToTemplate(templateRaw);
        NormalizeTemplate(template);
        ValidateTemplate(template);
        return template;
    }

    public CommandTemplate ImportFromText(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("模板内容不能为空。");
        }

        string trimmed = content.TrimStart();
        CommandTemplateRaw templateRaw;

        if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
        {
            templateRaw = ParseJson(content);
        }
        else
        {
            try
            {
                templateRaw = ParseYaml(content);
            }
            catch
            {
                templateRaw = ParseJson(content);
            }
        }

        CommandTemplate template = MapToTemplate(templateRaw);
        NormalizeTemplate(template);
        ValidateTemplate(template);
        return template;
    }

    private CommandTemplateRaw ParseJson(string content)
    {
        CommandTemplateRaw? template = JsonSerializer.Deserialize<CommandTemplateRaw>(content, _jsonOptions);
        return template ?? throw new InvalidOperationException("JSON 模板解析失败。");
    }

    private CommandTemplateRaw ParseYaml(string content)
    {
        CommandTemplateRaw? template = _yamlDeserializer.Deserialize<CommandTemplateRaw>(content);
        return template ?? throw new InvalidOperationException("YAML 模板解析失败。");
    }

    private static CommandTemplate MapToTemplate(CommandTemplateRaw raw)
    {
        return new CommandTemplate
        {
            Id = raw.Id ?? string.Empty,
            Name = raw.Name ?? string.Empty,
            Description = raw.Description ?? string.Empty,
            Command = raw.Command ?? string.Empty,
            WorkingDirectory = raw.WorkingDirectory ?? string.Empty,
            Arguments = (raw.Arguments ?? new List<TemplateArgumentRaw>()).Select(MapArgument).ToList()
        };
    }

    private static TemplateArgument MapArgument(TemplateArgumentRaw raw)
    {
        return new TemplateArgument
        {
            Key = raw.Key ?? string.Empty,
            Label = raw.Label ?? string.Empty,
            Type = ParseArgumentType(raw.Type),
            Required = raw.Required,
            DefaultValue = raw.DefaultValue ?? string.Empty,
            Options = raw.Options ?? new List<string>(),
            HelpText = raw.HelpText ?? string.Empty
        };
    }

    private static TemplateArgumentType ParseArgumentType(string? value)
    {
        string normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "text" or "string" => TemplateArgumentType.Text,
            "number" or "int" or "integer" or "float" or "double" => TemplateArgumentType.Number,
            "file" => TemplateArgumentType.File,
            "folder" or "directory" or "dir" => TemplateArgumentType.Folder,
            "select" or "dropdown" or "combo" or "combobox" => TemplateArgumentType.Select,
            "bool" or "boolean" or "switch" or "toggle" => TemplateArgumentType.Bool,
            _ => throw new InvalidOperationException($"不支持的参数类型：{value}")
        };
    }

    private static void NormalizeTemplate(CommandTemplate template)
    {
        template.Id = string.IsNullOrWhiteSpace(template.Id) ? Guid.NewGuid().ToString("N") : template.Id.Trim();
        template.Name = template.Name?.Trim() ?? string.Empty;
        template.Description = template.Description?.Trim() ?? string.Empty;
        template.Command = template.Command?.Trim() ?? string.Empty;
        template.WorkingDirectory = template.WorkingDirectory?.Trim() ?? string.Empty;
        template.Arguments ??= new List<TemplateArgument>();

        foreach (TemplateArgument arg in template.Arguments)
        {
            arg.Key = arg.Key?.Trim() ?? string.Empty;
            arg.Label = string.IsNullOrWhiteSpace(arg.Label) ? arg.Key : arg.Label.Trim();
            arg.DefaultValue ??= string.Empty;
            arg.HelpText ??= string.Empty;
            arg.Options ??= new List<string>();
        }
    }

    private static void ValidateTemplate(CommandTemplate template)
    {
        if (string.IsNullOrWhiteSpace(template.Name))
        {
            throw new InvalidOperationException("模板 Name 不能为空。");
        }

        if (string.IsNullOrWhiteSpace(template.Command))
        {
            throw new InvalidOperationException("模板 Command 不能为空。");
        }

        HashSet<string> keySet = new(StringComparer.OrdinalIgnoreCase);
        foreach (TemplateArgument arg in template.Arguments)
        {
            if (string.IsNullOrWhiteSpace(arg.Key))
            {
                throw new InvalidOperationException("模板参数 Key 不能为空。");
            }

            if (!keySet.Add(arg.Key))
            {
                throw new InvalidOperationException($"模板参数 Key 重复：{arg.Key}");
            }

            if (arg.Type == TemplateArgumentType.Select && (arg.Options is null || arg.Options.Count == 0))
            {
                throw new InvalidOperationException($"Select 参数必须提供 Options：{arg.Key}");
            }
        }
    }

    private sealed class CommandTemplateRaw
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Command { get; set; }
        public string? WorkingDirectory { get; set; }
        public List<TemplateArgumentRaw> Arguments { get; set; } = new();
    }

    private sealed class TemplateArgumentRaw
    {
        public string? Key { get; set; }
        public string? Label { get; set; }
        public string? Type { get; set; }
        public bool Required { get; set; }
        public string? DefaultValue { get; set; }
        public List<string>? Options { get; set; }
        public string? HelpText { get; set; }
    }
}
