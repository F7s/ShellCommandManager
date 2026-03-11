using System.Collections.Generic;

namespace ShellCommandManager.Models;

public sealed class TemplateArgument
{
    public string Key { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public TemplateArgumentType Type { get; set; } = TemplateArgumentType.Text;

    public bool Required { get; set; }

    public string DefaultValue { get; set; } = string.Empty;

    public List<string> Options { get; set; } = new();

    public string HelpText { get; set; } = string.Empty;
}
