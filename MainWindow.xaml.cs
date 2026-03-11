using ShellCommandManager.Models;
using ShellCommandManager.Services;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Reflection;
using Windows.Graphics;
using Windows.Storage.Pickers;

namespace ShellCommandManager
{
    public sealed partial class MainWindow : Window
    {
        private const int WindowHeight = 640;
        private const int CollapsedWindowWidth = 860;
        private const int ExpandedWindowWidth = 1240;
        private const int CollapsedMinWidth = 760;
        private const int ExpandedMinWidth = 1140;

        private readonly CommandStorageService _storageService = new();
        private readonly PowerShellCommandRunner _commandRunner = new();
        private readonly TemplateStorageService _templateStorageService = new();
        private readonly TemplateImportService _templateImportService = new();
        private readonly TemplateArgumentRenderService _templateArgumentRenderService = new();
        private readonly RuntimeValueHistoryService _runtimeValueHistoryService = new();
        private readonly UiSettingsService _uiSettingsService = new();
        private readonly DispatcherQueueTimer _statusAutoHideTimer;

        private static readonly string LogFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShellCommandManager",
            "logs",
            "app.log");

        private bool _isInitialized;
        private bool _isEditorVisible;
        private bool _isLanguageApplying;
        private int _currentMinWindowWidth = CollapsedMinWidth;
        private IntPtr _hwnd;
        private IntPtr _originalWndProc;
        private CommandTemplate? _selectedTemplate;
        private string _languageCode = string.Empty;
        private bool _blurBackgroundEnabled = true;
        private string _themePreference = "Default";

        private readonly Dictionary<string, FrameworkElement> _templateInputControls = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ToggleSwitch> _templateRuntimePromptToggles = new(StringComparer.OrdinalIgnoreCase);

        private const int WmGetMinMaxInfo = 0x0024;
        private const int GwlWndProc = -4;
        private static readonly IntPtr HwndTop = IntPtr.Zero;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;

        private static readonly WndProcDelegate WndProc = WindowProc;
        private static readonly Dictionary<IntPtr, MainWindow> WindowMap = new();

        public ObservableCollection<ShellCommandEntry> Commands { get; } = new();
        public ObservableCollection<CommandTemplate> Templates { get; } = new();

        public MainWindow()
        {
            InitializeComponent();
            _statusAutoHideTimer = DispatcherQueue.CreateTimer();
            _statusAutoHideTimer.Interval = TimeSpan.FromSeconds(5);
            _statusAutoHideTimer.IsRepeating = false;
            _statusAutoHideTimer.Tick += StatusAutoHideTimer_Tick;
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(TitleBarDragArea);
            TrySetWindowIcon();
            ConfigureTitleBarButtons();
            InitializeWindowMinSizeHook();
            SetEditorVisible(false);
            ResizeWindow(CollapsedWindowWidth);
        }

        private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isInitialized)
            {
                return;
            }

            _isInitialized = true;
            await InitializeVisualSettingsAsync();
            await InitializeLanguageAsync();
            ApplyLocalizedUi();
            await LoadTemplatesAsync();
            await LoadCommandsAsync();
        }

        private async Task LoadCommandsAsync()
        {
            try
            {
                Commands.Clear();
                IReadOnlyList<ShellCommandEntry> commands = await _storageService.LoadAsync();
                foreach (ShellCommandEntry command in commands)
                {
                    Commands.Add(command);
                }
            }
            catch (Exception ex)
            {
                await LogErrorAsync("LoadCommandsAsync", ex);
                ShowStatus(T("Status.LoadCommandsFailedSimple"), InfoBarSeverity.Error);
            }
        }

        private async Task LoadTemplatesAsync()
        {
            try
            {
                Templates.Clear();
                IReadOnlyList<CommandTemplate> templates = await _templateStorageService.LoadAsync();
                foreach (CommandTemplate template in templates)
                {
                    Templates.Add(template);
                }
            }
            catch (Exception ex)
            {
                await LogErrorAsync("LoadTemplatesAsync", ex);
                ShowStatus(T("Status.LoadTemplatesFailedSimple"), InfoBarSeverity.Error);
            }
        }

        private async void SaveCommandButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryReadEditorCommand(out ShellCommandEntry? editorCommand))
            {
                return;
            }

            int selectedIndex = CommandsListView.SelectedIndex;
            bool hasValidSelection = selectedIndex >= 0 && selectedIndex < Commands.Count;
            if (!hasValidSelection)
            {
                Commands.Add(editorCommand);
                CommandsListView.SelectedItem = null;
                ClearEditor();
                ShowStatus(T("Status.CommandAdded"), InfoBarSeverity.Success);
            }
            else
            {
                // Replace the selected item instead of mutating in place to ensure ListView refreshes reliably.
                Commands[selectedIndex] = editorCommand;
                CommandsListView.SelectedItem = Commands[selectedIndex];
                ShowStatus(T("Status.CommandUpdated"), InfoBarSeverity.Success);
            }

            await PersistCommandsAsync();
            UpdateSelectionActionVisibility();
        }

        private async void DeleteSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            List<ShellCommandEntry> selectedItems = GetSelectedCommands();
            if (selectedItems.Count == 0)
            {
                ShowStatus(T("Status.SelectBeforeDelete"), InfoBarSeverity.Warning);
                return;
            }

            foreach (ShellCommandEntry item in selectedItems)
            {
                Commands.Remove(item);
            }

            ClearEditor();
            await PersistCommandsAsync();
            ShowStatus(T("Status.CommandDeleted"), InfoBarSeverity.Success);
        }

        private async void RunSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            List<ShellCommandEntry> selectedCommands = GetSelectedCommands();
            if (selectedCommands.Count == 0)
            {
                ShellCommandEntry? selected = null;
                if (!TryReadEditorCommand(out selected))
                {
                    return;
                }

                selectedCommands.Add(selected);
            }

            foreach (ShellCommandEntry selected in selectedCommands)
            {
                try
                {
                    ShellCommandEntry commandToRun = selected;
                    if (TryGetTemplateById(selected.TemplateId, out CommandTemplate? template))
                    {
                        ShellCommandEntry? runtimeCommand = await BuildRuntimeCommandAsync(selected, template);
                        if (runtimeCommand is null)
                        {
                            continue;
                        }

                        commandToRun = runtimeCommand;
                    }

                    ShowStatus(string.Format(T("Status.RunningCommand"), selected.Name), InfoBarSeverity.Informational);
                    CommandRunResult result = await _commandRunner.RunAsync(commandToRun);
                    ShowStatus(string.Format(T("Status.CommandStartedWithPid"), commandToRun.Name, result.ProcessId), InfoBarSeverity.Success);
                }
                catch (Exception ex)
                {
                    await LogErrorAsync("RunSelectedButton_Click", ex);
                    ShowStatus(T("Status.RunFailedSimple"), InfoBarSeverity.Error);
                }
            }
        }

        private void ClearEditorButton_Click(object sender, RoutedEventArgs e)
        {
            CommandsListView.SelectedItem = null;
            ClearEditor();
            ShowStatus(T("Status.EditorCleared"), InfoBarSeverity.Informational);
        }

        private void ToggleEditorButton_Click(object sender, RoutedEventArgs e)
        {
            SetEditorVisible(!_isEditorVisible);
        }

        private async void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLanguageApplying)
            {
                return;
            }

            if (LanguageComboBox.SelectedItem is not ComboBoxItem item || item.Tag is not string languageCode)
            {
                return;
            }

            _languageCode = languageCode;
            ApplyLocalizedUi();

            try
            {
                await _uiSettingsService.SaveLanguageAsync(_languageCode);
            }
            catch (Exception ex)
            {
                await LogErrorAsync("LanguageComboBox_SelectionChanged", ex);
            }
        }

        private async void MoreButton_Click(object sender, RoutedEventArgs e)
        {
            await ShowMoreDialogAsync();
        }

        private void CommandsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateSelectionActionVisibility();

            if (CommandsListView.SelectedItems.Count != 1 || CommandsListView.SelectedItem is not ShellCommandEntry selected)
            {
                return;
            }

            NameTextBox.Text = selected.Name;
            CommandTextBox.Text = selected.PowerShellCommand;
            ArgsTextBox.Text = selected.StartupArguments;
            WorkingDirectoryTextBox.Text = selected.WorkingDirectory;

            if (TryGetTemplateById(selected.TemplateId, out CommandTemplate? template))
            {
                Dictionary<string, object?> values = BuildTemplateValues(template, selected.TemplateValuesJson);
                HashSet<string> runtimePromptKeys = ParseRuntimePromptKeys(selected.RuntimePromptKeysJson);
                TemplateComboBox.SelectedItem = template;
                SetTemplate(template, values, runtimePromptKeys);
            }
            else
            {
                TemplateComboBox.SelectedItem = null;
                SetTemplate(null);
            }
        }

        private async void ImportTemplateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                FileOpenPicker picker = new();
                picker.FileTypeFilter.Add(".json");
                picker.FileTypeFilter.Add(".yaml");
                picker.FileTypeFilter.Add(".yml");
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));

                var file = await picker.PickSingleFileAsync();
                if (file is null)
                {
                    return;
                }

                CommandTemplate template = await _templateImportService.ImportFromFileAsync(file.Path);
                UpsertTemplate(template);
                await PersistTemplatesAsync();
                TemplateComboBox.SelectedItem = template;
                ShowStatus(string.Format(T("Status.TemplateImported"), template.Name), InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                await LogErrorAsync("ImportTemplateButton_Click", ex);
                ShowStatus(T("Status.ImportTemplateFailedSimple"), InfoBarSeverity.Error);
            }
        }

        private async void ImportTemplateCodeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TextBox inputBox = new()
                {
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    PlaceholderText = T("Dialog.ImportTemplateCode.Placeholder"),
                    MinHeight = 280
                };

                ContentDialog dialog = new()
                {
                    XamlRoot = RootGrid.XamlRoot,
                    Title = T("Dialog.ImportTemplateCode.Title"),
                    PrimaryButtonText = T("Dialog.ImportTemplateCode.ImportButton"),
                    CloseButtonText = T("Dialog.ImportTemplateCode.CancelButton"),
                    DefaultButton = ContentDialogButton.Primary,
                    Content = inputBox
                };

                ContentDialogResult result = await dialog.ShowAsync();
                if (result != ContentDialogResult.Primary)
                {
                    return;
                }

                CommandTemplate template = _templateImportService.ImportFromText(inputBox.Text);
                UpsertTemplate(template);
                await PersistTemplatesAsync();
                TemplateComboBox.SelectedItem = template;
                ShowStatus(string.Format(T("Status.TemplateImported"), template.Name), InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                await LogErrorAsync("ImportTemplateCodeButton_Click", ex);
                ShowStatus(T("Status.ImportTemplateCodeFailedSimple"), InfoBarSeverity.Error);
            }
        }

        private async void ClearTemplateButton_Click(object sender, RoutedEventArgs e)
        {
            if (TemplateComboBox.SelectedItem is CommandTemplate selectedTemplate)
            {
                for (int i = 0; i < Templates.Count; i++)
                {
                    if (string.Equals(Templates[i].Id, selectedTemplate.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        Templates.RemoveAt(i);
                        break;
                    }
                }

                TemplateComboBox.SelectedItem = null;
                SetTemplate(null);
                await PersistTemplatesAsync();
                ShowStatus(string.Format(T("Status.TemplateDeleted"), selectedTemplate.Name), InfoBarSeverity.Success);
                return;
            }

            TemplateComboBox.SelectedItem = null;
            SetTemplate(null);
            ShowStatus(T("Status.TemplateCleared"), InfoBarSeverity.Informational);
        }

        private void TemplateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SetTemplate(TemplateComboBox.SelectedItem as CommandTemplate);
        }

        private void SetTemplate(
            CommandTemplate? template,
            IReadOnlyDictionary<string, object?>? initialValues = null,
            ISet<string>? runtimePromptKeys = null)
        {
            _selectedTemplate = template;
            _templateInputControls.Clear();
            _templateRuntimePromptToggles.Clear();
            TemplateArgsContainer.Children.Clear();

            if (template is null)
            {
                TemplateDescriptionTextBlock.Visibility = Visibility.Collapsed;
                ManualArgsPanel.Visibility = Visibility.Visible;
                TemplateArgsPanel.Visibility = Visibility.Collapsed;
                return;
            }

            if (!string.IsNullOrWhiteSpace(template.Command))
            {
                CommandTextBox.Text = template.Command;
            }

            if (!string.IsNullOrWhiteSpace(template.WorkingDirectory))
            {
                WorkingDirectoryTextBox.Text = template.WorkingDirectory;
            }

            TemplateDescriptionTextBlock.Text = string.IsNullOrWhiteSpace(template.Description) ? T("Template.Applied") : template.Description;
            TemplateDescriptionTextBlock.Visibility = Visibility.Visible;
            ManualArgsPanel.Visibility = Visibility.Collapsed;
            TemplateArgsPanel.Visibility = Visibility.Visible;

            foreach (TemplateArgument argument in template.Arguments)
            {
                CreateArgumentInput(argument, initialValues, runtimePromptKeys);
            }
        }

        private void CreateArgumentInput(
            TemplateArgument argument,
            IReadOnlyDictionary<string, object?>? initialValues,
            ISet<string>? runtimePromptKeys)
        {
            StackPanel container = new() { Spacing = 4 };
            string title = argument.Required ? $"{argument.Label} *" : argument.Label;
            container.Children.Add(new TextBlock { Text = title });

            string initialValue = ResolveInitialValue(argument, initialValues);
            FrameworkElement input = argument.Type switch
            {
                TemplateArgumentType.Number => CreateNumberInput(argument, initialValue),
                TemplateArgumentType.File => CreatePathInput(argument, true, initialValue),
                TemplateArgumentType.Folder => CreatePathInput(argument, false, initialValue),
                TemplateArgumentType.Select => CreateSelectInput(argument, initialValue),
                TemplateArgumentType.Bool => CreateBoolInput(initialValue),
                _ => CreateTextInput(argument, initialValue)
            };

            container.Children.Add(input);

            if (argument.Type is TemplateArgumentType.File or TemplateArgumentType.Folder)
            {
                bool isOn = runtimePromptKeys?.Contains(argument.Key) == true;
                ToggleSwitch runtimePromptToggle = new()
                {
                    Header = T("TemplateArg.AskBeforeRun"),
                    IsOn = isOn
                };
                container.Children.Add(runtimePromptToggle);
                _templateRuntimePromptToggles[argument.Key] = runtimePromptToggle;
            }

            if (!string.IsNullOrWhiteSpace(argument.HelpText))
            {
                container.Children.Add(new TextBlock { Text = argument.HelpText, Opacity = 0.75, TextWrapping = TextWrapping.WrapWholeWords });
            }

            TemplateArgsContainer.Children.Add(container);
            _templateInputControls[argument.Key] = input;
        }

        private static FrameworkElement CreateTextInput(TemplateArgument argument, string initialValue)
        {
            return new TextBox
            {
                PlaceholderText = argument.Key,
                Text = initialValue
            };
        }

        private static FrameworkElement CreateNumberInput(TemplateArgument argument, string initialValue)
        {
            NumberBox box = new() { PlaceholderText = argument.Key };
            if (double.TryParse(initialValue, out double num))
            {
                box.Value = num;
            }

            return box;
        }

        private FrameworkElement CreatePathInput(TemplateArgument argument, bool isFile, string initialValue)
        {
            Grid grid = new() { ColumnSpacing = 8 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBox pathBox = new() { PlaceholderText = argument.Key, Text = initialValue };
            Grid.SetColumn(pathBox, 0);

            Button pickButton = new() { Content = isFile ? T("Common.SelectFile") : T("Common.SelectFolder") };
            Grid.SetColumn(pickButton, 1);
            pickButton.Click += async (_, _) =>
            {
                string? path = isFile ? await PickFilePathAsync() : await PickFolderPathAsync();
                if (!string.IsNullOrWhiteSpace(path))
                {
                    pathBox.Text = path;
                }
            };

            grid.Children.Add(pathBox);
            grid.Children.Add(pickButton);
            return grid;
        }

        private static FrameworkElement CreateSelectInput(TemplateArgument argument, string initialValue)
        {
            ComboBox combo = new();
            foreach (string option in argument.Options)
            {
                combo.Items.Add(option);
            }

            if (!string.IsNullOrWhiteSpace(initialValue) && argument.Options.Contains(initialValue))
            {
                combo.SelectedItem = initialValue;
            }
            else if (combo.Items.Count > 0)
            {
                combo.SelectedIndex = 0;
            }

            return combo;
        }

        private static FrameworkElement CreateBoolInput(string initialValue)
        {
            bool enabled = bool.TryParse(initialValue, out bool value) && value;
            return new ToggleSwitch { IsOn = enabled };
        }

        private async Task<string?> PickFilePathAsync()
        {
            FileOpenPicker picker = new();
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file = await picker.PickSingleFileAsync();
            return file?.Path;
        }

        private async Task<string?> PickFolderPathAsync()
        {
            FolderPicker picker = new();
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var folder = await picker.PickSingleFolderAsync();
            return folder?.Path;
        }

        private bool TryReadEditorCommand([NotNullWhen(true)] out ShellCommandEntry? command)
        {
            command = null;
            string name = NameTextBox.Text.Trim();
            string shellCommand = CommandTextBox.Text.Trim();
            string workingDirectory = WorkingDirectoryTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                ShowStatus(T("Validation.NameRequired"), InfoBarSeverity.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(shellCommand))
            {
                ShowStatus(T("Validation.CommandRequired"), InfoBarSeverity.Warning);
                return false;
            }

            if (!string.IsNullOrWhiteSpace(workingDirectory) && !Directory.Exists(workingDirectory))
            {
                ShowStatus(T("Validation.WorkingDirectoryNotFound"), InfoBarSeverity.Warning);
                return false;
            }

            string startupArgs;
            string templateId = string.Empty;
            string valuesJson = string.Empty;
            string runtimePromptKeysJson = string.Empty;

            if (_selectedTemplate is not null)
            {
                if (!TryCollectTemplateValues(_selectedTemplate, out Dictionary<string, object?> values, out string validationError))
                {
                    ShowStatus(validationError, InfoBarSeverity.Warning);
                    return false;
                }

                startupArgs = _templateArgumentRenderService.BuildStartupArguments(_selectedTemplate, values);
                templateId = _selectedTemplate.Id;
                valuesJson = SerializeTemplateValues(values);
                runtimePromptKeysJson = SerializeRuntimePromptKeys();
            }
            else
            {
                startupArgs = ArgsTextBox.Text.Trim();
            }

            command = new ShellCommandEntry
            {
                TemplateId = templateId,
                TemplateValuesJson = valuesJson,
                RuntimePromptKeysJson = runtimePromptKeysJson,
                Name = name,
                PowerShellCommand = shellCommand,
                StartupArguments = startupArgs,
                WorkingDirectory = workingDirectory
            };

            return true;
        }

        private bool TryCollectTemplateValues(CommandTemplate template, out Dictionary<string, object?> values, out string error)
        {
            values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            error = string.Empty;

            foreach (TemplateArgument argument in template.Arguments)
            {
                if (!_templateInputControls.TryGetValue(argument.Key, out FrameworkElement? control))
                {
                    error = string.Format(T("Validation.TemplateControlMissing"), argument.Key);
                    return false;
                }

                object? rawValue = argument.Type switch
                {
                    TemplateArgumentType.Number => ReadNumberValue(control, argument),
                    TemplateArgumentType.File or TemplateArgumentType.Folder => ReadPathValue(control),
                    TemplateArgumentType.Select => ReadSelectValue(control),
                    TemplateArgumentType.Bool => ReadBoolValue(control),
                    _ => ReadTextValue(control)
                };

                if (argument.Required && argument.Type != TemplateArgumentType.Bool)
                {
                    string text = rawValue?.ToString()?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        error = string.Format(T("Validation.ArgumentRequired"), argument.Label);
                        return false;
                    }
                }

                if (argument.Type == TemplateArgumentType.Number && rawValue is not null)
                {
                    string numberText = rawValue.ToString()?.Trim() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(numberText) && !TryParseFlexibleNumber(numberText))
                    {
                        error = string.Format(T("Validation.ArgumentMustBeNumber"), argument.Label);
                        return false;
                    }
                }

                if (argument.Type == TemplateArgumentType.Select && argument.Required && string.IsNullOrWhiteSpace(rawValue?.ToString()))
                {
                    error = string.Format(T("Validation.ArgumentMustSelect"), argument.Label);
                    return false;
                }

                values[argument.Key] = rawValue;
            }

            return true;
        }

        private bool TryGetTemplateById(string templateId, [NotNullWhen(true)] out CommandTemplate? template)
        {
            template = null;
            if (string.IsNullOrWhiteSpace(templateId))
            {
                return false;
            }

            foreach (CommandTemplate item in Templates)
            {
                if (string.Equals(item.Id, templateId, StringComparison.OrdinalIgnoreCase))
                {
                    template = item;
                    return true;
                }
            }

            return false;
        }

        private async Task<ShellCommandEntry?> BuildRuntimeCommandAsync(ShellCommandEntry sourceCommand, CommandTemplate template)
        {
            Dictionary<string, object?> values = BuildTemplateValues(template, sourceCommand.TemplateValuesJson);
            HashSet<string> runtimePromptKeys = ParseRuntimePromptKeys(sourceCommand.RuntimePromptKeysJson);
            if (!await PromptRuntimeFileArgumentsAsync(sourceCommand, template, values, runtimePromptKeys))
            {
                return null;
            }

            string startupArgs = _templateArgumentRenderService.BuildStartupArguments(template, values);
            string valuesJson = SerializeTemplateValues(values);

            return new ShellCommandEntry
            {
                TemplateId = sourceCommand.TemplateId,
                TemplateValuesJson = valuesJson,
                RuntimePromptKeysJson = sourceCommand.RuntimePromptKeysJson,
                Name = sourceCommand.Name,
                PowerShellCommand = sourceCommand.PowerShellCommand,
                StartupArguments = startupArgs,
                WorkingDirectory = sourceCommand.WorkingDirectory
            };
        }

        private Dictionary<string, object?> BuildTemplateValues(CommandTemplate template, string templateValuesJson)
        {
            Dictionary<string, object?> values = new(StringComparer.OrdinalIgnoreCase);
            foreach (TemplateArgument argument in template.Arguments)
            {
                values[argument.Key] = ConvertStringValueForType(argument, argument.DefaultValue);
            }

            if (string.IsNullOrWhiteSpace(templateValuesJson))
            {
                return values;
            }

            Dictionary<string, string>? persistedValues = null;
            try
            {
                persistedValues = JsonSerializer.Deserialize<Dictionary<string, string>>(templateValuesJson);
            }
            catch
            {
                // Ignore broken persisted payload and fallback to defaults.
            }

            if (persistedValues is null)
            {
                return values;
            }

            foreach (TemplateArgument argument in template.Arguments)
            {
                if (persistedValues.TryGetValue(argument.Key, out string? rawText))
                {
                    values[argument.Key] = ConvertStringValueForType(argument, rawText);
                }
            }

            return values;
        }

        private async Task<bool> PromptRuntimeFileArgumentsAsync(
            ShellCommandEntry sourceCommand,
            CommandTemplate template,
            Dictionary<string, object?> values,
            ISet<string> runtimePromptKeys)
        {
            List<TemplateArgument> fileArguments = new();
            foreach (TemplateArgument argument in template.Arguments)
            {
                if (argument.Type is TemplateArgumentType.File or TemplateArgumentType.Folder
                    && runtimePromptKeys.Contains(argument.Key))
                {
                    fileArguments.Add(argument);
                }
            }

            if (fileArguments.Count == 0)
            {
                return true;
            }

            string commandKey = BuildRuntimeCommandKey(sourceCommand);
            StackPanel panel = new() { Spacing = 12 };
            Dictionary<string, TextBox> inputMap = new(StringComparer.OrdinalIgnoreCase);

            foreach (TemplateArgument argument in fileArguments)
            {
                TextBlock label = new()
                {
                    Text = argument.Required ? $"{argument.Label} *" : argument.Label
                };

                string initialValue = values.TryGetValue(argument.Key, out object? value) ? value?.ToString() ?? string.Empty : string.Empty;
                (Grid row, TextBox input) = await CreateRuntimePathRowAsync(argument, commandKey, initialValue);

                panel.Children.Add(label);
                panel.Children.Add(row);

                if (!string.IsNullOrWhiteSpace(argument.HelpText))
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = argument.HelpText,
                        Opacity = 0.75,
                        TextWrapping = TextWrapping.WrapWholeWords
                    });
                }

                inputMap[argument.Key] = input;
            }

            ContentDialog dialog = new()
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = T("Dialog.RuntimeArgs.Title"),
                PrimaryButtonText = T("Dialog.RuntimeArgs.RunButton"),
                CloseButtonText = T("Dialog.RuntimeArgs.CancelButton"),
                DefaultButton = ContentDialogButton.Primary,
                Content = new ScrollViewer
                {
                    Content = panel,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    MaxHeight = 360
                }
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return false;
            }

            foreach (TemplateArgument argument in fileArguments)
            {
                string text = inputMap[argument.Key].Text.Trim();
                if (argument.Required && string.IsNullOrWhiteSpace(text))
                {
                    ShowStatus(string.Format(T("Validation.ArgumentRequired"), argument.Label), InfoBarSeverity.Warning);
                    return false;
                }

                values[argument.Key] = text;

                if (!string.IsNullOrWhiteSpace(text))
                {
                    await _runtimeValueHistoryService.SaveRecentValueAsync(commandKey, argument.Key, text);
                }
            }

            return true;
        }

        private async Task<(Grid Row, TextBox Input)> CreateRuntimePathRowAsync(
            TemplateArgument argument,
            string commandKey,
            string initialValue)
        {
            Grid row = new() { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBox input = new()
            {
                PlaceholderText = argument.Key,
                Text = initialValue
            };

            IReadOnlyList<string> recent = await _runtimeValueHistoryService.GetRecentValuesAsync(commandKey, argument.Key);

            Grid.SetColumn(input, 0);

            Button browseButton = new()
            {
                Content = argument.Type == TemplateArgumentType.File ? T("Common.SelectFile") : T("Common.SelectFolder")
            };
            Grid.SetColumn(browseButton, 1);
            browseButton.Click += async (_, _) =>
            {
                string? path = argument.Type == TemplateArgumentType.File
                    ? await PickFilePathAsync()
                    : await PickFolderPathAsync();
                if (!string.IsNullOrWhiteSpace(path))
                {
                    input.Text = path;
                }
            };

            Button historyButton = new()
            {
                Content = T("Common.History"),
                IsEnabled = recent.Count > 0
            };
            Grid.SetColumn(historyButton, 2);
            historyButton.Click += (_, _) =>
            {
                MenuFlyout flyout = new();
                if (recent.Count == 0)
                {
                    flyout.Items.Add(new MenuFlyoutItem
                    {
                        Text = T("Common.NoHistory"),
                        IsEnabled = false
                    });
                }
                else
                {
                    foreach (string item in recent)
                    {
                        MenuFlyoutItem flyoutItem = new() { Text = item };
                        flyoutItem.Click += (_, _) => input.Text = item;
                        flyout.Items.Add(flyoutItem);
                    }
                }

                flyout.ShowAt(historyButton);
            };

            row.Children.Add(input);
            row.Children.Add(browseButton);
            row.Children.Add(historyButton);
            return (row, input);
        }

        private static string SerializeTemplateValues(IReadOnlyDictionary<string, object?> values)
        {
            Dictionary<string, string> payload = new(StringComparer.OrdinalIgnoreCase);
            foreach ((string key, object? value) in values)
            {
                if (value is bool boolValue)
                {
                    payload[key] = boolValue ? "true" : "false";
                }
                else
                {
                    payload[key] = value?.ToString() ?? string.Empty;
                }
            }

            return JsonSerializer.Serialize(payload);
        }

        private static object? ConvertStringValueForType(TemplateArgument argument, string? rawValue)
        {
            string value = rawValue?.Trim() ?? string.Empty;
            if (argument.Type == TemplateArgumentType.Bool)
            {
                return bool.TryParse(value, out bool boolValue) && boolValue;
            }

            return value;
        }

        private static string ResolveInitialValue(TemplateArgument argument, IReadOnlyDictionary<string, object?>? initialValues)
        {
            if (initialValues is not null
                && initialValues.TryGetValue(argument.Key, out object? rawValue))
            {
                if (rawValue is bool boolValue)
                {
                    return boolValue ? "true" : "false";
                }

                return rawValue?.ToString() ?? string.Empty;
            }

            return argument.DefaultValue ?? string.Empty;
        }

        private string SerializeRuntimePromptKeys()
        {
            if (_templateRuntimePromptToggles.Count == 0)
            {
                return string.Empty;
            }

            List<string> keys = new();
            foreach ((string key, ToggleSwitch toggle) in _templateRuntimePromptToggles)
            {
                if (toggle.IsOn)
                {
                    keys.Add(key);
                }
            }

            return keys.Count == 0 ? string.Empty : JsonSerializer.Serialize(keys);
        }

        private static HashSet<string> ParseRuntimePromptKeys(string runtimePromptKeysJson)
        {
            if (string.IsNullOrWhiteSpace(runtimePromptKeysJson))
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                List<string>? keys = JsonSerializer.Deserialize<List<string>>(runtimePromptKeysJson);
                if (keys is null)
                {
                    return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }

                return new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static string BuildRuntimeCommandKey(ShellCommandEntry command)
        {
            return $"{command.TemplateId}|{command.Name}|{command.PowerShellCommand}";
        }

        private static object? ReadTextValue(FrameworkElement control)
        {
            return control is TextBox box ? box.Text.Trim() : string.Empty;
        }

        private static object? ReadNumberValue(FrameworkElement control, TemplateArgument argument)
        {
            if (control is not NumberBox numberBox)
            {
                return string.Empty;
            }

            if (double.IsNaN(numberBox.Value))
            {
                return string.Empty;
            }

            bool preferDecimal = argument.DefaultValue?.Contains('.') == true;
            return numberBox.Value.ToString(preferDecimal ? "0.################" : "0");
        }

        private static object? ReadPathValue(FrameworkElement control)
        {
            if (control is Grid grid && grid.Children.Count > 0 && grid.Children[0] is TextBox box)
            {
                return box.Text.Trim();
            }

            return string.Empty;
        }

        private static object? ReadSelectValue(FrameworkElement control)
        {
            return control is ComboBox combo ? combo.SelectedItem?.ToString() ?? string.Empty : string.Empty;
        }

        private static object? ReadBoolValue(FrameworkElement control)
        {
            return control is ToggleSwitch toggle && toggle.IsOn;
        }

        private static bool TryParseFlexibleNumber(string value)
        {
            if (double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out _))
            {
                return true;
            }

            if (double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out _))
            {
                return true;
            }

            string swapped = value.Contains(',') ? value.Replace(',', '.') : value.Replace('.', ',');
            return double.TryParse(swapped, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out _)
                || double.TryParse(swapped, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out _);
        }

        private async Task PersistCommandsAsync()
        {
            await _storageService.SaveAsync(Commands);
        }

        private async Task PersistTemplatesAsync()
        {
            await _templateStorageService.SaveAsync(Templates);
        }

        private void UpsertTemplate(CommandTemplate template)
        {
            for (int i = 0; i < Templates.Count; i++)
            {
                if (string.Equals(Templates[i].Id, template.Id, StringComparison.OrdinalIgnoreCase))
                {
                    Templates[i] = template;
                    return;
                }
            }

            Templates.Add(template);
        }

        private void ClearEditor()
        {
            SetTemplate(null);
            TemplateComboBox.SelectedItem = null;
            NameTextBox.Text = string.Empty;
            CommandTextBox.Text = string.Empty;
            ArgsTextBox.Text = string.Empty;
            WorkingDirectoryTextBox.Text = string.Empty;
        }

        private void ShowStatus(string message, InfoBarSeverity severity)
        {
            StatusInfoBar.Severity = severity;
            StatusInfoBar.Message = message;
            StatusInfoBar.IsOpen = true;
            _statusAutoHideTimer.Stop();
            _statusAutoHideTimer.Start();
        }

        private void StatusAutoHideTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            sender.Stop();
            StatusInfoBar.IsOpen = false;
        }

        private async Task ShowMoreDialogAsync()
        {
            string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";

            TextBlock versionLabel = new()
            {
                Text = string.Format(T("More.VersionText"), version),
                TextWrapping = TextWrapping.Wrap
            };

            ComboBox languageBox = new() { MinWidth = 220 };
            languageBox.Items.Add(new ComboBoxItem { Content = "简体中文", Tag = "zh-CN" });
            languageBox.Items.Add(new ComboBoxItem { Content = "English", Tag = "en-US" });
            SetLanguageComboSelection(languageBox, _languageCode);
            ComboBox themeBox = new() { MinWidth = 220 };
            themeBox.Items.Add(new ComboBoxItem { Content = T("More.Theme.System"), Tag = "Default" });
            themeBox.Items.Add(new ComboBoxItem { Content = T("More.Theme.Light"), Tag = "Light" });
            themeBox.Items.Add(new ComboBoxItem { Content = T("More.Theme.Dark"), Tag = "Dark" });
            SetLanguageComboSelection(themeBox, _themePreference);
            ToggleSwitch blurToggle = new()
            {
                IsOn = _blurBackgroundEnabled
            };

            Button exportRulesButton = new()
            {
                Content = T("More.ExportTemplateRules")
            };
            exportRulesButton.Click += async (_, _) => await ExportTemplateRulesAsync();

            StackPanel panel = new() { Spacing = 12 };
            panel.Children.Add(versionLabel);
            panel.Children.Add(new TextBlock { Text = T("More.Language"), Opacity = 0.8 });
            panel.Children.Add(languageBox);
            panel.Children.Add(new TextBlock { Text = T("More.Theme"), Opacity = 0.8 });
            panel.Children.Add(themeBox);
            panel.Children.Add(new TextBlock { Text = T("More.BlurBackground"), Opacity = 0.8 });
            panel.Children.Add(blurToggle);
            panel.Children.Add(exportRulesButton);

            ContentDialog dialog = new()
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = string.Empty,
                PrimaryButtonText = T("Common.Confirm"),
                CloseButtonText = T("Dialog.RuntimeArgs.CancelButton"),
                DefaultButton = ContentDialogButton.Primary,
                Content = panel
            };

            ContentDialogResult result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary
                && languageBox.SelectedItem is ComboBoxItem item
                && item.Tag is string languageCode)
            {
                await ApplyLanguageCodeAsync(languageCode);
            }

            if (result == ContentDialogResult.Primary)
            {
                if (themeBox.SelectedItem is ComboBoxItem themeItem && themeItem.Tag is string theme)
                {
                    await ApplyThemeAsync(theme);
                }

                await ApplyBlurBackgroundSettingAsync(blurToggle.IsOn);
            }
        }

        private async Task ApplyLanguageCodeAsync(string languageCode)
        {
            _languageCode = languageCode;
            ApplyLanguageSelectionUi();
            ApplyLocalizedUi();
            await _uiSettingsService.SaveLanguageAsync(_languageCode);
        }

        private async Task InitializeVisualSettingsAsync()
        {
            try
            {
                _blurBackgroundEnabled = await _uiSettingsService.LoadBlurBackgroundEnabledAsync() ?? true;
                _themePreference = await _uiSettingsService.LoadThemeAsync() ?? "Default";
            }
            catch (Exception ex)
            {
                await LogErrorAsync("InitializeVisualSettingsAsync", ex);
                _blurBackgroundEnabled = true;
                _themePreference = "Default";
            }

            ApplyThemePreference();
            ApplyBackdrop();
        }

        private async Task ApplyThemeAsync(string theme)
        {
            _themePreference = string.IsNullOrWhiteSpace(theme) ? "Default" : theme;
            ApplyThemePreference();
            await _uiSettingsService.SaveThemeAsync(_themePreference);
        }

        private void ApplyThemePreference()
        {
            ElementTheme theme = _themePreference switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default
            };

            RootGrid.RequestedTheme = theme;
            if (Content is FrameworkElement contentRoot)
            {
                contentRoot.RequestedTheme = theme;
            }
        }

        private async Task ApplyBlurBackgroundSettingAsync(bool enabled)
        {
            _blurBackgroundEnabled = enabled;
            ApplyBackdrop();
            await _uiSettingsService.SaveBlurBackgroundEnabledAsync(_blurBackgroundEnabled);
        }

        private void ApplyBackdrop()
        {
            SystemBackdrop = _blurBackgroundEnabled ? new MicaBackdrop() : null;
        }

        private static void SetLanguageComboSelection(ComboBox comboBox, string languageCode)
        {
            foreach (object item in comboBox.Items)
            {
                if (item is ComboBoxItem comboItem
                    && comboItem.Tag is string tag
                    && string.Equals(tag, languageCode, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedItem = comboItem;
                    return;
                }
            }

            if (comboBox.Items.Count > 0)
            {
                comboBox.SelectedIndex = 0;
            }
        }

        private async Task ExportTemplateRulesAsync()
        {
            try
            {
                string sourcePath = Path.Combine(AppContext.BaseDirectory, "TEMPLATE_RULES.md");
                if (!File.Exists(sourcePath))
                {
                    sourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TEMPLATE_RULES.md");
                }

                if (!File.Exists(sourcePath))
                {
                    // Fallback to project-relative path while debugging.
                    sourcePath = Path.Combine(Environment.CurrentDirectory, "TEMPLATE_RULES.md");
                }

                if (!File.Exists(sourcePath))
                {
                    ShowStatus(T("Status.TemplateRulesNotFound"), InfoBarSeverity.Warning);
                    return;
                }

                FileSavePicker picker = new();
                picker.FileTypeChoices.Add("Markdown", new List<string> { ".md" });
                picker.SuggestedFileName = "TEMPLATE_RULES";
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));

                var file = await picker.PickSaveFileAsync();
                if (file is null)
                {
                    return;
                }

                string content = await File.ReadAllTextAsync(sourcePath);
                await File.WriteAllTextAsync(file.Path, content);
                ShowStatus(string.Format(T("Status.TemplateRulesExported"), file.Name), InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                await LogErrorAsync("ExportTemplateRulesAsync", ex);
                ShowStatus(T("Status.TemplateRulesExportFailed"), InfoBarSeverity.Error);
            }
        }

        private string T(string key)
        {
            bool zh = IsChineseUi();
            return key switch
            {
                "Status.CommandsLoaded" => zh ? "已加载 {0} 条命令。" : "Loaded {0} command(s).",
                "Status.LoadCommandsFailedSimple" => zh ? "加载命令失败，请检查命令配置文件。" : "Failed to load commands. Check the command file.",
                "Status.LoadTemplatesFailedSimple" => zh ? "加载模板失败，请检查模板文件格式。" : "Failed to load templates. Check template format.",
                "Status.CommandAdded" => zh ? "已新增命令，可继续添加下一条。" : "Command added. You can add the next one.",
                "Status.CommandUpdated" => zh ? "已更新命令。" : "Command updated.",
                "Status.SelectBeforeDelete" => zh ? "请先选择一条命令再删除。" : "Select a command before deleting.",
                "Status.CommandDeleted" => zh ? "已删除命令。" : "Command deleted.",
                "Status.RunningCommand" => zh ? "正在运行：{0}" : "Running: {0}",
                "Status.CommandStartedWithPid" => zh ? "已在 PowerShell 窗口启动：{0} (PID: {1})" : "Started in PowerShell: {0} (PID: {1})",
                "Status.RunFailedSimple" => zh ? "执行失败，请检查命令与参数后重试。" : "Run failed. Check command and arguments.",
                "Status.EditorCleared" => zh ? "输入区域已清空。" : "Editor cleared.",
                "Status.TemplateImported" => zh ? "模板导入成功：{0}" : "Template imported: {0}",
                "Status.TemplateDeleted" => zh ? "模板已删除：{0}" : "Template deleted: {0}",
                "Status.TemplateCleared" => zh ? "已清空当前模板应用。" : "Cleared current template selection.",
                "Status.ImportTemplateFailedSimple" => zh ? "导入模板失败，请检查 JSON/YAML 结构。" : "Template import failed. Check JSON/YAML structure.",
                "Status.ImportTemplateCodeFailedSimple" => zh ? "代码导入失败，请检查粘贴内容格式。" : "Code import failed. Check pasted content.",
                "Status.TemplateRulesNotFound" => zh ? "未找到模板规则文档 TEMPLATE_RULES.md。" : "Template rules file TEMPLATE_RULES.md was not found.",
                "Status.TemplateRulesExported" => zh ? "模板规则已导出：{0}" : "Template rules exported: {0}",
                "Status.TemplateRulesExportFailed" => zh ? "导出模板规则失败。" : "Failed to export template rules.",
                "Dialog.ImportTemplateCode.Placeholder" => zh ? "粘贴 JSON/YAML 模板内容" : "Paste JSON/YAML template content",
                "Dialog.ImportTemplateCode.Title" => zh ? "代码导入模板" : "Import Template from Code",
                "Dialog.ImportTemplateCode.ImportButton" => zh ? "导入" : "Import",
                "Dialog.ImportTemplateCode.CancelButton" => zh ? "取消" : "Cancel",
                "Template.Applied" => zh ? "已应用模板" : "Template applied",
                "TemplateArg.AskBeforeRun" => zh ? "运行前询问" : "Ask before run",
                "Common.SelectFile" => zh ? "选择文件" : "Select file",
                "Common.SelectFolder" => zh ? "选择文件夹" : "Select folder",
                "Common.History" => zh ? "历史" : "History",
                "Common.NoHistory" => zh ? "暂无历史" : "No history",
                "Validation.NameRequired" => zh ? "名称不能为空。" : "Name is required.",
                "Validation.CommandRequired" => zh ? "PowerShell 命令不能为空。" : "PowerShell command is required.",
                "Validation.WorkingDirectoryNotFound" => zh ? "工作目录不存在，请检查路径。" : "Working directory not found.",
                "Validation.TemplateControlMissing" => zh ? "模板参数控件缺失：{0}" : "Template argument control missing: {0}",
                "Validation.ArgumentRequired" => zh ? "参数“{0}”为必填。" : "Argument \"{0}\" is required.",
                "Validation.ArgumentMustBeNumber" => zh ? "参数“{0}”必须是数字。" : "Argument \"{0}\" must be a number.",
                "Validation.ArgumentMustSelect" => zh ? "参数“{0}”必须选择一个选项。" : "Argument \"{0}\" must select an option.",
                "Dialog.RuntimeArgs.Title" => zh ? "运行前参数" : "Runtime Arguments",
                "Dialog.RuntimeArgs.RunButton" => zh ? "运行" : "Run",
                "Dialog.RuntimeArgs.CancelButton" => zh ? "取消" : "Cancel",
                "AppBar.CollapseAdd" => zh ? "收起添加" : "Collapse Add",
                "AppBar.AddCommand" => zh ? "添加命令" : "Add Command",
                "MainWindow.Title" => zh ? "Shell 命令管理器" : "Shell Command Manager",
                "Editor.Title" => zh ? "命令配置" : "Command Configuration",
                "Editor.Template" => zh ? "命令模板" : "Command Template",
                "Editor.ImportTemplate" => zh ? "导入模板" : "Import Template",
                "Editor.ImportCode" => zh ? "代码导入" : "Code Import",
                "Editor.ClearTemplate" => zh ? "删除模板" : "Delete Template",
                "Editor.Name" => zh ? "名称" : "Name",
                "Editor.Name.Placeholder" => zh ? "例如：启动 API 服务" : "e.g. Start API service",
                "Editor.Command" => zh ? "PowerShell 命令" : "PowerShell Command",
                "Editor.Command.Placeholder" => zh ? "例如：dotnet run --project .\\Api\\Api.csproj" : "e.g. dotnet run --project .\\Api\\Api.csproj",
                "Editor.ManualArgs" => zh ? "启动参数（会附加到命令后）" : "Startup arguments (appended to command)",
                "Editor.ManualArgs.Placeholder" => zh ? "例如：--environment Development" : "e.g. --environment Development",
                "Editor.TemplateArgs" => zh ? "模板参数" : "Template Arguments",
                "Editor.WorkDir" => zh ? "工作目录（可选）" : "Working Directory (Optional)",
                "Editor.WorkDir.Placeholder" => zh ? "例如：C:\\Projects\\MyApp" : "e.g. C:\\Projects\\MyApp",
                "Editor.Template.Placeholder" => zh ? "选择已导入模板" : "Select an imported template",
                "List.Title" => zh ? "已保存命令" : "Saved Commands",
                "List.Hint" => zh ? "选中后可直接运行，也可编辑后点击“保存命令”覆盖更新。" : "Select a command to run directly, or edit and click Save to update.",
                "AppBar.SaveCommand" => zh ? "保存命令" : "Save Command",
                "AppBar.RunSelected" => zh ? "运行所选" : "Run Selected",
                "AppBar.DeleteSelected" => zh ? "删除所选" : "Delete Selected",
                "AppBar.ClearInput" => zh ? "清空输入" : "Clear Input",
                "AppBar.More" => zh ? "更多" : "More",
                "More.Title" => zh ? "更多" : "More",
                "More.VersionText" => zh ? "版本：{0}" : "Version: {0}",
                "More.Language" => zh ? "语言" : "Language",
                "More.Theme" => zh ? "主题" : "Theme",
                "More.Theme.System" => zh ? "跟随系统" : "Use system",
                "More.Theme.Light" => zh ? "浅色" : "Light",
                "More.Theme.Dark" => zh ? "深色" : "Dark",
                "More.BlurBackground" => zh ? "背景模糊（Mica）" : "Blur Background (Mica)",
                "More.ExportTemplateRules" => zh ? "导出模板规则" : "Export Template Rules",
                "Common.Confirm" => zh ? "确定" : "Confirm",
                _ => key
            };
        }

        private bool IsChineseUi()
        {
            string name = string.IsNullOrWhiteSpace(_languageCode) ? CultureInfo.CurrentUICulture.Name : _languageCode;
            return name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        }

        private async Task InitializeLanguageAsync()
        {
            try
            {
                _languageCode = await _uiSettingsService.LoadLanguageAsync() ?? string.Empty;
            }
            catch (Exception ex)
            {
                await LogErrorAsync("InitializeLanguageAsync", ex);
                _languageCode = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(_languageCode))
            {
                _languageCode = IsChineseUi() ? "zh-CN" : "en-US";
            }

            ApplyLanguageSelectionUi();
        }

        private void ApplyLanguageSelectionUi()
        {
            _isLanguageApplying = true;
            try
            {
                foreach (object item in LanguageComboBox.Items)
                {
                    if (item is ComboBoxItem comboBoxItem && comboBoxItem.Tag is string tag && string.Equals(tag, _languageCode, StringComparison.OrdinalIgnoreCase))
                    {
                        LanguageComboBox.SelectedItem = comboBoxItem;
                        return;
                    }
                }

                if (LanguageComboBox.Items.Count > 0)
                {
                    LanguageComboBox.SelectedIndex = 0;
                }
            }
            finally
            {
                _isLanguageApplying = false;
            }
        }

        private void ApplyLocalizedUi()
        {
            Title = T("MainWindow.Title");
            TitleText.Text = T("MainWindow.Title");
            EditorTitleTextBlock.Text = T("Editor.Title");
            TemplateLabelTextBlock.Text = T("Editor.Template");
            ImportTemplateButton.Content = T("Editor.ImportTemplate");
            ImportTemplateCodeButton.Content = T("Editor.ImportCode");
            ClearTemplateButton.Label = T("Editor.ClearTemplate");
            TemplateComboBox.PlaceholderText = T("Editor.Template.Placeholder");
            NameLabelTextBlock.Text = T("Editor.Name");
            NameTextBox.PlaceholderText = T("Editor.Name.Placeholder");
            CommandLabelTextBlock.Text = T("Editor.Command");
            CommandTextBox.PlaceholderText = T("Editor.Command.Placeholder");
            ManualArgsLabelTextBlock.Text = T("Editor.ManualArgs");
            ArgsTextBox.PlaceholderText = T("Editor.ManualArgs.Placeholder");
            TemplateArgsLabelTextBlock.Text = T("Editor.TemplateArgs");
            WorkingDirectoryLabelTextBlock.Text = T("Editor.WorkDir");
            WorkingDirectoryTextBox.PlaceholderText = T("Editor.WorkDir.Placeholder");
            SavedCommandsTitleTextBlock.Text = T("List.Title");
            SavedCommandsHintTextBlock.Text = T("List.Hint");
            SaveCommandAppBarButton.Label = T("AppBar.SaveCommand");
            RunSelectedAppBarButton.Label = T("AppBar.RunSelected");
            DeleteSelectedAppBarButton.Label = T("AppBar.DeleteSelected");
            ClearInputAppBarButton.Label = T("AppBar.ClearInput");
            MoreButton.Label = string.Empty;
            ToggleEditorButton.Label = _isEditorVisible ? T("AppBar.CollapseAdd") : T("AppBar.AddCommand");
        }

        private static async Task LogErrorAsync(string context, Exception ex)
        {
            try
            {
                string? directory = Path.GetDirectoryName(LogFilePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}{Environment.NewLine}{ex}{Environment.NewLine}";
                await File.AppendAllTextAsync(LogFilePath, line);
            }
            catch
            {
                // Avoid secondary failures from logging.
            }
        }

        private void UpdateSelectionActionVisibility()
        {
            bool hasSelection = CommandsListView.SelectedItems.Count > 0;
            RunSelectedAppBarButton.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
            DeleteSelectedAppBarButton.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
        }

        private List<ShellCommandEntry> GetSelectedCommands()
        {
            List<ShellCommandEntry> selectedCommands = new();
            foreach (object item in CommandsListView.SelectedItems)
            {
                if (item is ShellCommandEntry command)
                {
                    selectedCommands.Add(command);
                }
            }

            return selectedCommands;
        }

        private void SetEditorVisible(bool isVisible)
        {
            _isEditorVisible = isVisible;
            EditorPanel.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            SaveCommandAppBarButton.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            ClearInputAppBarButton.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            ImportTemplateButton.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            ImportTemplateCodeButton.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            ClearTemplateButton.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            EditorColumn.Width = isVisible ? new GridLength(420) : new GridLength(0);
            ToggleEditorButton.Label = isVisible ? T("AppBar.CollapseAdd") : T("AppBar.AddCommand");
            ToggleEditorButton.Icon = new SymbolIcon(isVisible ? Symbol.Remove : Symbol.Add);
            Grid.SetColumn(ListPanel, isVisible ? 1 : 0);
            Grid.SetColumnSpan(ListPanel, isVisible ? 1 : 2);

            if (!isVisible)
            {
                CommandsListView.SelectedItem = null;
                ClearEditor();
            }

            UpdateSelectionActionVisibility();

            _currentMinWindowWidth = isVisible ? ExpandedMinWidth : CollapsedMinWidth;
            // Keep window geometry stable; only toggle panel visibility/layout.
        }

        private void ResizeWindow(int width)
        {
            AppWindow.Resize(new SizeInt32(width, WindowHeight));
        }

        private void ResizeWindowAnchoredRight(int width)
        {
            SizeInt32 currentSize = AppWindow.Size;
            PointInt32 currentPosition = AppWindow.Position;
            int newX = currentPosition.X + (currentSize.Width - width);
            if (_hwnd != IntPtr.Zero)
            {
                SetWindowPos(_hwnd, HwndTop, newX, currentPosition.Y, width, WindowHeight, SwpNoZOrder | SwpNoActivate);
                return;
            }

            AppWindow.Move(new PointInt32(newX, currentPosition.Y));
            AppWindow.Resize(new SizeInt32(width, WindowHeight));
        }

        // Intentionally no window resize/move on editor toggle.

        private void ConfigureTitleBarButtons()
        {
            if (!AppWindowTitleBar.IsCustomizationSupported())
            {
                return;
            }

            var titleBar = AppWindow.TitleBar;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.BackgroundColor = Colors.Transparent;
            titleBar.InactiveBackgroundColor = Colors.Transparent;
        }

        private void TrySetWindowIcon()
        {
            try
            {
                string iconPath = Path.Combine(AppContext.BaseDirectory, "AppIcon.ico");
                if (File.Exists(iconPath))
                {
                    AppWindow.SetIcon(iconPath);
                }
            }
            catch
            {
                // Keep startup resilient if icon cannot be applied.
            }
        }

        private void InitializeWindowMinSizeHook()
        {
            _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WindowMap[_hwnd] = this;
            _originalWndProc = SetWindowLongPtr(_hwnd, GwlWndProc, Marshal.GetFunctionPointerForDelegate(WndProc));
            Closed += MainWindow_Closed;
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            if (_hwnd != IntPtr.Zero)
            {
                SetWindowLongPtr(_hwnd, GwlWndProc, _originalWndProc);
                WindowMap.Remove(_hwnd);
                _hwnd = IntPtr.Zero;
            }
        }

        private static IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WmGetMinMaxInfo && WindowMap.TryGetValue(hWnd, out MainWindow? window))
            {
                MINMAXINFO info = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                info.ptMinTrackSize.x = window._currentMinWindowWidth;
                Marshal.StructureToPtr(info, lParam, true);
            }

            if (WindowMap.TryGetValue(hWnd, out MainWindow? ownerWindow) && ownerWindow._originalWndProc != IntPtr.Zero)
            {
                return CallWindowProc(ownerWindow._originalWndProc, hWnd, msg, wParam, lParam);
            }

            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int X,
            int Y,
            int cx,
            int cy,
            uint uFlags);
    }
}
