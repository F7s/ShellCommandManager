using ShellCommandManager.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace ShellCommandManager.Services;

public sealed class CommandRunResult
{
    public int ProcessId { get; init; }
}

public sealed class PowerShellCommandRunner
{
    public async Task<CommandRunResult> RunAsync(ShellCommandEntry command)
    {
        if (string.IsNullOrWhiteSpace(command.PowerShellCommand))
        {
            throw new ArgumentException("命令不能为空。", nameof(command));
        }

        if (!string.IsNullOrWhiteSpace(command.WorkingDirectory) && !Directory.Exists(command.WorkingDirectory))
        {
            throw new DirectoryNotFoundException($"工作目录不存在：{command.WorkingDirectory}");
        }

        string fullCommand = string.IsNullOrWhiteSpace(command.StartupArguments)
            ? command.PowerShellCommand
            : $"{command.PowerShellCommand} {command.StartupArguments}";

        string refreshPathScript =
            "$machinePath = [Environment]::GetEnvironmentVariable('Path','Machine');" +
            "$userPath = [Environment]::GetEnvironmentVariable('Path','User');" +
            "if ($null -eq $machinePath) { $machinePath = '' };" +
            "if ($null -eq $userPath) { $userPath = '' };" +
            "$env:Path = ($machinePath + ';' + $userPath).Trim(';');";
        string script = $"{refreshPathScript} {fullCommand}";
        string encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

        ProcessStartInfo startInfo = new()
        {
            FileName = "powershell.exe",
            Arguments = $"-NoLogo -NoExit -ExecutionPolicy Bypass -EncodedCommand {encodedScript}",
            UseShellExecute = true,
            CreateNoWindow = false
        };

        if (!string.IsNullOrWhiteSpace(command.WorkingDirectory))
        {
            startInfo.WorkingDirectory = command.WorkingDirectory;
        }

        Process process = new()
        {
            StartInfo = startInfo
        };

        process.Start();
        await Task.CompletedTask;

        return new CommandRunResult
        {
            ProcessId = process.Id
        };
    }
}
