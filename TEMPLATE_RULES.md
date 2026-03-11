# 命令模板规则（优化版）

本文档描述 `Shell 命令管理器` 的模板格式、校验规则和最佳实践，适用于：
- 文件导入：`.json` / `.yaml` / `.yml`
- 代码导入：粘贴 JSON/YAML 文本

## 1. 先看最小可用模板

```json
{
  "name": "My Command",
  "command": "python",
  "arguments": [
    { "key": "script.py", "type": "text" }
  ]
}
```

只要 `name` 和 `command` 合法，就可以导入。`id` 可以省略，系统会自动生成。

## 2. 顶层字段定义

模板必须是一个对象（Object）：

- `id`: `string`，可选  
  模板唯一标识。为空时自动生成（GUID 字符串）。
- `name`: `string`，必填  
  模板名称（UI 下拉框显示）。
- `description`: `string`，可选  
  模板说明（选择模板后在编辑区显示）。
- `command`: `string`，必填  
  基础可执行命令（例如 `python`、`.\llama-server.exe`）。
- `workingDirectory`: `string`，可选  
  默认工作目录。
- `arguments`: `array`，可选  
  参数定义列表，元素为 `TemplateArgument`。

## 3. 参数字段定义（arguments[]）

每个参数对象支持以下字段：

- `key`: `string`，必填  
  参数键，如 `--port`、`-m`、`--host`。
- `label`: `string`，可选  
  UI 显示名。为空时自动回退为 `key`。
- `type`: `string`，必填  
  参数类型（见第 4 节）。
- `required`: `boolean`，可选，默认 `false`  
  是否必填（`bool` 类型在保存时不做“非空”必填判定）。
- `defaultValue`: `string`，可选，默认空字符串  
  默认值。  
  注意：建议始终写成字符串（包括数字和布尔），避免解析差异。
- `options`: `string[]`，可选  
  仅 `select` 类型使用，且不能为空。
- `helpText`: `string`，可选  
  参数说明提示文本。

## 4. `type` 支持值与别名（不区分大小写）

- 文本：`text`、`string`
- 数字：`number`、`int`、`integer`、`float`、`double`
- 文件：`file`
- 文件夹：`folder`、`directory`、`dir`
- 下拉单选：`select`、`dropdown`、`combo`、`combobox`
- 布尔开关：`bool`、`boolean`、`switch`、`toggle`

## 5. UI 控件映射（模板 -> 界面）

- `Text` -> `TextBox`
- `Number` -> `NumberBox`
- `File` -> `TextBox + 选择文件按钮`
- `Folder` -> `TextBox + 选择文件夹按钮`
- `Select` -> `ComboBox`
- `Bool` -> `ToggleSwitch`

## 6. 参数拼接规则（生成 StartupArguments）

保存命令时，参数会渲染成启动参数字符串：

- 默认格式：`key value`（例：`--port 8080`）
- `Bool`：值为 `true` 仅输出 `key`；值为 `false` 不输出
- 空值不输出
- 文件/文件夹路径含空格时自动加双引号

## 7. 导入校验规则

导入时会进行结构校验，常见规则如下：

- `name` 不能为空
- `command` 不能为空
- `arguments[].key` 不能为空，且同一模板内不能重复
- `select` 类型必须提供非空 `options`
- `type` 必须是受支持类型或别名

## 8. 常见错误与修复

- 错误：`JSON value could not be converted to ...TemplateArgumentType`  
  原因：`type` 写了未支持值  
  修复：改为第 4 节支持值（如 `Text` / `Number` / `File` / `Folder` / `Select` / `Bool`）

- 错误：`Select 参数必须提供 Options`  
  原因：`type=select` 但 `options` 为空  
  修复：至少提供 1 个选项

- 错误：`模板参数 Key 重复`  
  原因：多个参数使用了同一 `key`  
  修复：保证每个参数 `key` 唯一

- 保存时提示“参数必须是数字”  
  原因：`number` 参数输入了非数字字符  
  修复：只输入数字（支持小数）

## 9. 当前能力边界

- 支持模板导入与本地持久化（JSON 统一存储）
- 支持代码导入（粘贴 JSON/YAML）
- 运行前询问（如文件参数是否弹窗）目前由“命令编辑界面”配置，非模板字段

## 10. 推荐写法（完整 JSON 示例）

```json
{
  "id": "llama-server",
  "name": "Llama Server",
  "description": "启动 llama.cpp 服务端，支持模型、mmproj、端口等参数",
  "command": ".\\llama-server.exe",
  "workingDirectory": "C:\\Projects\\llama\\bin",
  "arguments": [
    {
      "key": "-m",
      "label": "模型文件",
      "type": "File",
      "required": true,
      "defaultValue": "C:\\Models\\qwen2.5-7b-instruct-q4_k_m.gguf",
      "helpText": "选择 GGUF 模型文件"
    },
    {
      "key": "--mmproj",
      "label": "MMProj 文件",
      "type": "File",
      "defaultValue": "C:\\Models\\mmproj-model-f16.gguf",
      "helpText": "多模态模型需要；纯文本模型可留空"
    },
    {
      "key": "--host",
      "label": "监听地址",
      "type": "Text",
      "defaultValue": "127.0.0.1"
    },
    {
      "key": "--port",
      "label": "端口",
      "type": "Number",
      "defaultValue": "8080"
    },
    {
      "key": "--mode",
      "label": "模式",
      "type": "Select",
      "options": ["dev", "prod"],
      "defaultValue": "dev"
    },
    {
      "key": "--ctx-size-fixed",
      "label": "固定上下文缓存",
      "type": "Bool",
      "defaultValue": "false"
    }
  ]
}
```

## 11. YAML 对应示例

```yaml
id: llama-server
name: Llama Server
description: 启动 llama.cpp 服务端，支持模型、mmproj、端口等参数
command: .\llama-server.exe
workingDirectory: C:\Projects\llama\bin
arguments:
  - key: -m
    label: 模型文件
    type: file
    required: true
    defaultValue: C:\Models\qwen2.5-7b-instruct-q4_k_m.gguf
    helpText: 选择 GGUF 模型文件
  - key: --mmproj
    label: MMProj 文件
    type: file
    defaultValue: C:\Models\mmproj-model-f16.gguf
    helpText: 多模态模型需要；纯文本模型可留空
  - key: --port
    label: 端口
    type: number
    defaultValue: "8080"
  - key: --mode
    label: 模式
    type: select
    options: [dev, prod]
    defaultValue: dev
  - key: --ctx-size-fixed
    label: 固定上下文缓存
    type: bool
    defaultValue: "false"
```
