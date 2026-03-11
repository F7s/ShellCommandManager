namespace App1.Models;

public sealed class ShellCommandEntry
{
    public string TemplateId { get; set; } = string.Empty;

    public string TemplateValuesJson { get; set; } = string.Empty;

    public string RuntimePromptKeysJson { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string PowerShellCommand { get; set; } = string.Empty;

    public string StartupArguments { get; set; } = string.Empty;

    public string WorkingDirectory { get; set; } = string.Empty;
}
