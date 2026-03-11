# Shell Command Manager (WinUI 3)

一个用于管理和运行 PowerShell 命令的 WinUI 3 桌面应用。  
核心目标是把“常用命令 + 启动参数 + 运行前输入”标准化，减少重复手工拼接参数。

## 功能概览

- 命令管理：添加、保存、编辑、删除、批量运行
- 模板系统：支持 JSON/YAML 文件导入、代码粘贴导入、模板删除
- 动态参数表单：按模板自动渲染参数输入控件
- 运行前参数：可按字段配置“运行前询问”，并支持历史值回填
- 参数渲染：自动生成最终命令行参数（兼容既有执行逻辑）
- 执行体验：运行时直接弹出 PowerShell 窗口，便于实时观察输出
- 国际化：内置简体中文/英文切换
- 更多面板：版本信息、模板规则导出、语言切换入口

## 技术栈

- .NET 8
- WinUI 3 (Windows App SDK)
- C#
- YamlDotNet（模板 YAML 解析）
- Inno Setup / WiX（安装包构建）

## 项目结构

- `App1/ShellCommandManager.csproj`：WinUI 项目文件
- `App1/MainWindow.xaml`：主界面布局
- `App1/MainWindow.xaml.cs`：主交互逻辑（命令、模板、运行流程）
- `App1/Models/`：数据模型（命令、模板、参数）
- `App1/Services/`：存储、导入、参数渲染、执行服务
- `App1/Strings/zh-CN/Resources.resw`：中文资源
- `App1/Strings/en-US/Resources.resw`：英文资源
- `App1/TEMPLATE_RULES.md`：模板定义规则
- `installer/setup-inno/`：Inno Setup 安装脚本
- `installer/out/`：安装包输出目录

## 环境要求

- Windows 10 19041+ / Windows 11
- .NET SDK 8.x（用于开发构建）
- Visual Studio 2022/2026（推荐，含 WinUI 工作负载）

## 本地开发与运行

在仓库根目录执行：

```powershell
dotnet restore .\App1\ShellCommandManager.csproj
dotnet build .\App1\ShellCommandManager.csproj -c Debug -p:Platform=x64
dotnet run --project .\App1\ShellCommandManager.csproj -c Debug -p:Platform=x64
```

常见平台参数：

- `-p:Platform=x64`
- `-p:Platform=ARM64`

## 发布可运行 EXE

### x64（框架依赖，体积较小）

```powershell
dotnet publish .\App1\ShellCommandManager.csproj -c Release -r win-x64 -p:Platform=x64 --self-contained false
```

输出目录：

- `App1/bin/Release/net8.0-windows10.0.19041.0/win-x64/publish/`

### arm64（框架依赖，体积较小）

```powershell
dotnet publish .\App1\ShellCommandManager.csproj -c Release -r win-arm64 -p:Platform=ARM64 --self-contained false
```

输出目录：

- `App1/bin/Release/net8.0-windows10.0.19041.0/win-arm64/publish/`

### 单文件大 EXE（自包含）

```powershell
dotnet publish .\App1\ShellCommandManager.csproj -c Release -r win-x64 -p:Platform=x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:EnableMsixTooling=true
```

说明：

- WinUI 3 + Windows App SDK 自包含单文件体积通常较大（100MB+），这是预期行为。

## 生成安装包

### Inno Setup（标准 Setup）

脚本位置：

- `installer/setup-inno/ShellCommandManager.iss`（x64）
- `installer/setup-inno/ShellCommandManager.arm64.iss`（arm64）

命令示例：

```powershell
& "C:\ProgramData\chocolatey\bin\ISCC.exe" ".\installer\setup-inno\ShellCommandManager.iss"
& "C:\ProgramData\chocolatey\bin\ISCC.exe" ".\installer\setup-inno\ShellCommandManager.arm64.iss"
```

输出目录：

- `installer/out/`

## 模板系统说明

模板支持类型：

- `Text`
- `Number`
- `File`
- `Folder`
- `Select`
- `Bool`

导入方式：

- 导入模板（读取 `.json/.yaml/.yml` 文件）
- 代码导入（粘贴 JSON/YAML 文本）

参数渲染规则（默认）：

- 文本/数字/选择项：`key value`
- 布尔：`true` 输出 `key`，`false` 不输出
- 路径参数：必要时自动加引号避免空格路径执行失败

详情见：

- `App1/TEMPLATE_RULES.md`

## 数据存储位置

默认保存到：

- `%LocalAppData%\ShellCommandManager\shell-commands.json`
- `%LocalAppData%\ShellCommandManager\command-templates.json`
- `%LocalAppData%\ShellCommandManager\runtime-value-history.json`
- `%LocalAppData%\ShellCommandManager\ui-settings.json`

## 国际化

- 当前支持 `zh-CN`、`en-US`
- 语言设置在 UI 中切换并持久化
- 新增语言时补充 `Strings/<locale>/Resources.resw`，并对齐代码中的键

## 常见问题

### 1) 模板导入失败：`type` 无法转换

请确认 `type` 使用以下值之一：`Text|Number|File|Folder|Select|Bool`。  
并确认 `Select` 类型提供了非空 `options`。

### 2) “端口必须是数字”但我输入的是数字

请检查是否包含空格、中文标点或其他非数字字符。  
建议直接输入纯数字（例如 `8080`）。

### 3) 命令无法识别环境变量中的程序

请优先确认：

- 命令在系统 PowerShell 中可直接执行
- `workingDirectory` 是否正确
- 必要时使用命令完整路径

### 4) 构建失败提示文件被占用

关闭已运行的 `ShellCommandManager.exe` 后重试构建。

## Roadmap（建议）

- 模板版本化（`SchemaVersion`）
- 参数渲染规则可配置化（非默认 key-value）
- 模板市场/共享导入（本地文件以外）
- 命令分组与标签筛选
