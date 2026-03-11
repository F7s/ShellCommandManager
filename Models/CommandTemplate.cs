using System;
using System.Collections.Generic;

namespace App1.Models;

public sealed class CommandTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Command { get; set; } = string.Empty;

    public string WorkingDirectory { get; set; } = string.Empty;

    public List<TemplateArgument> Arguments { get; set; } = new();

    public override string ToString() => Name;
}
