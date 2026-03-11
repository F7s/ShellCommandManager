# Shell Command Manager (WinUI 3)

A WinUI 3 desktop app for managing and running PowerShell commands with template-based arguments.

## Features

- Save, edit, delete, and run shell commands
- Template import (`JSON` / `YAML`) from file or pasted code
- Dynamic argument form rendering:
  - `Text`, `Number`, `File`, `Folder`, `Select`, `Bool`
- Runtime prompt support for file/folder arguments
- Multi-select command run support
- Local persistence for commands, templates, and runtime histories
- Chinese/English UI adaptation

## Project Structure

- `ShellCommandManager.csproj`: project file
- `MainWindow.xaml` / `MainWindow.xaml.cs`: main UI and logic
- `Models/`: command and template models
- `Services/`: storage, import, rendering, runner services
- `Strings/`: localized resources
- `TEMPLATE_RULES.md`: template schema and usage rules

## Build

```powershell
dotnet build .\ShellCommandManager.csproj -p:Platform=x64
```

ARM64 build:

```powershell
dotnet build .\ShellCommandManager.csproj -c Release -p:Platform=ARM64
```

## Run

```powershell
dotnet run --project .\ShellCommandManager.csproj -p:Platform=x64
```

## Local Data

Stored under `%LocalAppData%\\App1\\`:

- `shell-commands.json`
- `command-templates.json`
- `runtime-value-history.json`
- `ui-settings.json`
