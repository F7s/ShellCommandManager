# App1 - Shell 命令管理器（WinUI 3）

一个基于 WinUI 3 的桌面应用，用于集中管理并运行常用 PowerShell 命令，支持模板化参数配置、运行前动态参数输入和本地持久化。

## 主要功能

- 命令管理：新增、编辑、删除、选择运行
- 模板导入：支持 JSON / YAML 文件导入与代码粘贴导入
- 模板参数渲染：`Text` / `Number` / `File` / `Folder` / `Select` / `Bool`
- 启动参数生成：自动按模板生成 `StartupArguments`
- 运行前询问：可对文件/文件夹参数单独配置“运行前询问”
- 历史值回填：运行前参数支持历史记录快速选择
- 可见 PowerShell 窗口：运行时直接弹出 shell 窗口
- 国际化：已适配 `zh-CN` 与 `en-US` 资源

## 项目结构

- `App1/App1.csproj`：项目文件
- `App1/MainWindow.xaml`：主界面
- `App1/MainWindow.xaml.cs`：交互逻辑
- `App1/Models/`：数据模型（命令、模板、参数）
- `App1/Services/`：存储、导入、渲染、运行服务
- `App1/Strings/zh-CN/Resources.resw`：中文资源
- `App1/Strings/en-US/Resources.resw`：英文资源
- `App1/TEMPLATE_RULES.md`：模板定义规则文档
- `App1/I18N_CHECKLIST.md`：国际化回归清单

## 运行与构建

在项目根目录执行：

```powershell
dotnet build .\App1\App1.csproj -p:Platform=x64
dotnet run --project .\App1\App1.csproj -p:Platform=x64
```

说明：
- 如遇 `App1.exe` 被占用导致构建失败，请先关闭正在运行的应用进程后重试。

## 本地数据存储

应用数据默认保存在：

- `%LocalAppData%\App1\shell-commands.json`（已保存命令）
- `%LocalAppData%\App1\command-templates.json`（模板）
- `%LocalAppData%\App1\runtime-value-history.json`（运行前参数历史）
- `%LocalAppData%\App1\ui-settings.json`（UI 语言设置）

## 模板使用

请参考：

- [模板规则文档](C:/Users/lzh/Documents/Project_file/App1/App1/TEMPLATE_RULES.md)

支持导入方式：

- UI 按钮“导入模板”（文件）
- UI 按钮“代码导入”（粘贴 JSON/YAML）

## 国际化说明

- 支持应用内语言切换：`简体中文` / `English`
- 语言选择会持久化到 `%LocalAppData%\App1\ui-settings.json`
- XAML 静态文案通过控件映射更新
- 代码侧提示文案通过统一 `T(key)` 管理（中英双语）

如需新增语言，可添加：

- `App1/Strings/<locale>/Resources.resw`
- 并同步补齐 `MainWindow.xaml.cs` 中 `T(key)` 的对应键值。

## 常见问题

- 模板导入失败  
  - 检查 `type` 是否为支持值（见 `TEMPLATE_RULES.md`）
  - `select` 类型必须提供非空 `options`

- 参数提示“必须是数字”  
  - 该参数类型为 `Number`，请仅输入数字（支持小数）

- 命令无法执行  
  - 检查 `PowerShell 命令` 与 `工作目录`
  - 确认命令在系统环境变量中可用，或使用完整路径
