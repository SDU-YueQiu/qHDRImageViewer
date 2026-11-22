// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.WinUI.Controls;
using HdrImageViewer.Models;
using HdrImageViewer.Rendering;
using HdrImageViewer.Services;
using HdrImageViewer.Settings;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.Storage.Pickers;
using Windows.System;
using Color = Windows.UI.Color;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace HdrImageViewer.Pages;

/// <summary>
/// Navigation-based settings page. The page itself contains no hard-coded
/// settings: every card is generated from <see cref="SettingsRegistry"/>.
/// </summary>
public sealed partial class SettingsPage : Page
{
    private const int HighlightDurationMilliseconds = 1500;
    private const int PointerFocusDelayMilliseconds = 50;
    private const double NumberClampTolerance = 0.0001;

    private readonly SettingsViewModel _viewModel = new();
    private readonly Dictionary<string, FrameworkElement> _cards = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Control> _valueControls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> _resetButtons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Expander> _settingExpanders = new(StringComparer.Ordinal);
    private readonly List<SettingDefinition> _currentValueDefinitions = [];
    private readonly List<SettingsSearchResult> _searchResults = [];

    private SettingsPageDefinition? _currentPage;
    private DispatcherQueueTimer? _highlightTimer;
    private DispatcherQueueTimer? _pointerFocusTimer;
    private Control? _highlightedCard;
    private bool _isLoaded;
    private bool _isUpdatingControls;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += SettingsPage_Loaded;
    }

    private static string Loc(string key) => Localization.GetString(key);

    private static string Loc(string key, params object[] args) => Localization.GetString(key, args);

    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        LocalizeStaticText();
        BuildNavigation();
        _viewModel.SettingChanged += ViewModel_SettingChanged;
        _viewModel.SettingsReloaded += ViewModel_SettingsReloaded;

        if (SettingsNavigation.MenuItems.Count > 0)
        {
            SettingsNavigation.SelectedItem = SettingsNavigation.MenuItems[0];
        }
    }

    private void LocalizeStaticText()
    {
        SearchBox.PlaceholderText = Loc("SettingsSearchPlaceholder");
        AutomationProperties.SetName(SearchBox, Loc("SettingsSearchPlaceholder"));
        SearchHintText.Text = Loc("SettingsSearchHint");
        AutomationProperties.SetName(this, Loc("SettingsPageTitle.Text"));
        AutomationProperties.SetName(SettingsNavigation, Loc("SettingsPageTitle.Text"));
        RestartNowButton.Content = Loc("SettingsRestartNow.Content");
        AutomationProperties.SetName(RestartNowButton, Loc("SettingsRestartNow.Content"));
        AutomationProperties.SetName(SettingsInfoBar, Loc("SettingsPageTitle.Text"));
    }

    private void BuildNavigation()
    {
        foreach (var page in SettingsRegistry.Pages)
        {
            var item = new NavigationViewItem
            {
                Content = Loc(page.LabelResourceKey),
                Icon = new FontIcon { Glyph = page.Glyph },
                Tag = page.Id,
            };
            AutomationProperties.SetAutomationId(item, "SettingsNav" + Capitalize(page.Id));
            SettingsNavigation.MenuItems.Add(item);
        }
    }

    private static string Capitalize(string value)
    {
        return string.IsNullOrEmpty(value)
            ? string.Empty
            : char.ToUpperInvariant(value[0]) + value[1..];
    }

    private void SettingsNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string pageId })
        {
            ShowPage(pageId);
        }
    }

    private void ShowPage(string pageId)
    {
        var page = SettingsRegistry.GetPage(pageId);
        _currentPage = page;
        BuildPage(page);
    }

    private void BuildPage(SettingsPageDefinition page)
    {
        _cards.Clear();
        _valueControls.Clear();
        _resetButtons.Clear();
        _settingExpanders.Clear();
        _currentValueDefinitions.Clear();
        PageContentHost.Children.Clear();

        PageContentHost.Children.Add(new TextBlock
        {
            Text = Loc(page.LabelResourceKey),
            Style = (Style)Resources["SettingsPageTitleStyle"],
            Margin = new Thickness(0, 0, 0, 8),
        });

        foreach (var group in page.Groups)
        {
            if (group.IsCollapsible)
            {
                AddCollapsibleGroup(group);
            }
            else
            {
                AddGroup(group);
            }
        }

        AddPageResetButton(page);
        RefreshAllControlValues();
    }

    private void AddGroup(SettingsGroupDefinition group)
    {
        PageContentHost.Children.Add(CreateGroupHeader(group.LabelResourceKey));
        foreach (var definition in group.Settings)
        {
            PageContentHost.Children.Add(CreateCard(definition));
        }
    }

    private void AddCollapsibleGroup(SettingsGroupDefinition group)
    {
        var panel = new StackPanel { Spacing = 4 };
        foreach (var definition in group.Settings)
        {
            panel.Children.Add(CreateCard(definition));
        }

        var expander = new Expander
        {
            Header = Loc(group.LabelResourceKey),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 16, 0, 4),
            IsExpanded = false,
            Content = panel,
        };
        AutomationProperties.SetAutomationId(expander, "SettingsExpander" + Capitalize(group.Id[(group.Id.IndexOf('.') + 1)..]));
        foreach (var definition in group.Settings)
        {
            _settingExpanders[definition.Id] = expander;
        }

        PageContentHost.Children.Add(expander);
    }

    private TextBlock CreateGroupHeader(string labelResourceKey)
    {
        return new TextBlock
        {
            Text = Loc(labelResourceKey),
            Style = (Style)Resources["SettingsGroupHeaderStyle"],
            Margin = new Thickness(0, 16, 0, 4),
        };
    }

    private FrameworkElement CreateCard(SettingDefinition definition)
    {
        if (definition.Kind == SettingKind.Info)
        {
            return CreateInfoBlock(definition);
        }

        var card = new SettingsCard
        {
            Header = Loc(definition.LabelResourceKey),
            HeaderIcon = new FontIcon { Glyph = definition.Glyph },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 0, 4),
        };
        if (definition.DescriptionResourceKey is not null)
        {
            card.Description = Loc(definition.DescriptionResourceKey);
        }

        AutomationProperties.SetAutomationId(card, "SettingsCard" + definition.Id);
        _cards[definition.Id] = card;

        if (definition.Kind == SettingKind.Button)
        {
            var button = new Button
            {
                Content = definition.ActionLabelResourceKey is null ? Loc(definition.LabelResourceKey) : Loc(definition.ActionLabelResourceKey),
                MinWidth = 96,
                Tag = definition,
            };
            AutomationProperties.SetAutomationId(button, definition.AutomationId);
            AutomationProperties.SetName(button, Loc(definition.LabelResourceKey));
            button.Click += ActionButton_Click;
            card.Content = button;
            return card;
        }

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var valueControl = CreateValueControl(definition);
        content.Children.Add(valueControl);

        if (definition.UnitResourceKey is not null)
        {
            content.Children.Add(new TextBlock
            {
                Text = Loc(definition.UnitResourceKey),
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.7,
            });
        }

        var resetButton = CreateResetButton(definition);
        content.Children.Add(resetButton);
        card.Content = content;

        _valueControls[definition.Id] = valueControl;
        _resetButtons[definition.Id] = resetButton;
        if (definition.Kind != SettingKind.Button && definition.Kind != SettingKind.Info)
        {
            _currentValueDefinitions.Add(definition);
        }

        return card;
    }

    private Button CreateResetButton(SettingDefinition definition)
    {
        var resetButton = new Button
        {
            Content = new FontIcon { Glyph = "\uE7A7", FontSize = 14 },
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            IsEnabled = SettingsViewModel.IsModifiedFromDefault(definition),
            Tag = definition,
        };
        AutomationProperties.SetAutomationId(resetButton, "SettingsReset" + definition.Id);
        AutomationProperties.SetName(resetButton, Loc("SettingsResetToDefaultToolTip"));
        ToolTipService.SetToolTip(resetButton, Loc("SettingsResetToDefaultToolTip"));
        resetButton.Click += ResetButton_Click;
        return resetButton;
    }

    private Control CreateValueControl(SettingDefinition definition)
    {
        switch (definition.Kind)
        {
            case SettingKind.Toggle:
            {
                var toggle = new ToggleSwitch
                {
                    OnContent = Loc("SettingsToggleOn"),
                    OffContent = Loc("SettingsToggleOff"),
                    MinWidth = 120,
                    Tag = definition,
                };
                AutomationProperties.SetAutomationId(toggle, definition.AutomationId);
                AutomationProperties.SetName(toggle, Loc(definition.LabelResourceKey));
                toggle.Toggled += Toggle_Toggled;
                return toggle;
            }

            case SettingKind.Choice:
            {
                var comboBox = new ComboBox
                {
                    MinWidth = 180,
                    MaxWidth = 280,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Tag = definition,
                };
                foreach (var choice in definition.Choices)
                {
                    comboBox.Items.Add(new ComboBoxItem
                    {
                        Content = Loc(choice.LabelResourceKey),
                        Tag = choice.Value,
                    });
                }

                AutomationProperties.SetAutomationId(comboBox, definition.AutomationId);
                AutomationProperties.SetName(comboBox, Loc(definition.LabelResourceKey));
                comboBox.SelectionChanged += Choice_SelectionChanged;
                return comboBox;
            }

            case SettingKind.Number:
            {
                var numberBox = new NumberBox
                {
                    Minimum = definition.Minimum,
                    Maximum = definition.Maximum,
                    SmallChange = definition.Step,
                    LargeChange = definition.Step * 5,
                    SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
                    MinWidth = 160,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Tag = definition,
                };
                AutomationProperties.SetAutomationId(numberBox, definition.AutomationId);
                AutomationProperties.SetName(numberBox, Loc(definition.LabelResourceKey));
                numberBox.ValueChanged += Number_ValueChanged;
                numberBox.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(NumberSetting_KeyDown), handledEventsToo: true);
                return numberBox;
            }

            default:
            {
                var textBox = new TextBox
                {
                    MinWidth = 180,
                    Tag = definition,
                };
                AutomationProperties.SetAutomationId(textBox, definition.AutomationId);
                AutomationProperties.SetName(textBox, Loc(definition.LabelResourceKey));
                textBox.TextChanged += TextSetting_TextChanged;
                textBox.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(TextSetting_KeyDown), handledEventsToo: true);
                return textBox;
            }
        }
    }

    private FrameworkElement CreateInfoBlock(SettingDefinition definition)
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(new TextBlock
        {
            Text = Loc(definition.LabelResourceKey),
            Style = (Style)Resources["SettingsGroupHeaderStyle"],
        });

        if (definition.DescriptionResourceKey is not null)
        {
            panel.Children.Add(new TextBlock
            {
                Text = Loc(definition.DescriptionResourceKey),
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.8,
            });
        }

        var text = new TextBlock
        {
            Text = definition.InfoTextProvider?.Invoke() ?? string.Empty,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            FontFamily = new FontFamily("Consolas"),
        };
        AutomationProperties.SetAutomationId(text, "SettingsDiagnosticsText");
        panel.Children.Add(text);

        var border = new Border
        {
            Child = panel,
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(48, 128, 128, 128)),
            Background = new SolidColorBrush(Color.FromArgb(16, 128, 128, 128)),
            Margin = new Thickness(0, 0, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetAutomationId(border, "SettingsCard" + definition.Id);
        _cards[definition.Id] = border;
        return border;
    }

    private void AddPageResetButton(SettingsPageDefinition page)
    {
        if (page.Id == SettingsRegistry.PageAdvanced
            || !page.Settings.Any(static setting => setting.Kind is SettingKind.Toggle or SettingKind.Choice or SettingKind.Number))
        {
            return;
        }

        var button = new Button
        {
            Content = Loc("SettingsResetPageButton"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 24, 0, 8),
            Tag = page.Id,
        };
        AutomationProperties.SetAutomationId(button, "SettingsResetPageButton");
        button.Click += ResetPageButton_Click;
        PageContentHost.Children.Add(button);
    }

    private void RefreshAllControlValues()
    {
        _isUpdatingControls = true;
        try
        {
            foreach (var definition in _currentValueDefinitions)
            {
                SetControlValueCore(definition);
            }
        }
        finally
        {
            _isUpdatingControls = false;
        }

        UpdateAllResetButtons();
        UpdateDependentControlStates();
    }

    private void RefreshControlValue(SettingDefinition definition)
    {
        _isUpdatingControls = true;
        try
        {
            SetControlValueCore(definition);
        }
        finally
        {
            _isUpdatingControls = false;
        }

        UpdateResetButton(definition);
        UpdateDependentControlStates();
    }

    private void SetControlValueCore(SettingDefinition definition)
    {
        if (!_valueControls.TryGetValue(definition.Id, out var control))
        {
            return;
        }

        var value = SettingsViewModel.GetValue(definition);
        switch (control)
        {
            case ToggleSwitch toggle:
                toggle.IsOn = value is bool isOn && isOn;
                break;
            case ComboBox comboBox:
                comboBox.SelectedIndex = SettingChoiceMatcher.FindChoiceIndex(definition.Choices, value);
                break;
            case NumberBox numberBox:
                numberBox.Value = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                break;
            case TextBox textBox:
                textBox.Text = value as string ?? string.Empty;
                break;
        }
    }

    private void UpdateAllResetButtons()
    {
        foreach (var (id, button) in _resetButtons)
        {
            if (SettingsRegistry.FindSetting(id) is { } definition)
            {
                button.IsEnabled = SettingsViewModel.IsModifiedFromDefault(definition) && IsControlEnabled(id);
            }
        }
    }

    private void UpdateResetButton(SettingDefinition definition)
    {
        if (_resetButtons.TryGetValue(definition.Id, out var button))
        {
            button.IsEnabled = SettingsViewModel.IsModifiedFromDefault(definition) && IsControlEnabled(definition.Id);
        }
    }

    private void UpdateDependentControlStates()
    {
        var settings = AppSettingsService.Current;
        SetControlEnabled("AdjacentPreloadRadius", settings.PreloadAdjacentImages);
        SetControlEnabled("HdrManualHeadroomStops", settings.HdrHeadroomMode == HdrHeadroomMode.Manual);
        SetControlEnabled("SdrWhiteNits", settings.SdrWhiteOverrideEnabled);
        SetControlEnabled("RtxVsrMode", settings.RtxVsrMenuEnabled);
        SetControlEnabled("RtxVsrQualityPreset", settings.RtxVsrMenuEnabled);
        SetControlEnabled("RtxVsrScaleFactor", IsRtxVsrScaleFactorEnabled(settings));
    }

    private void SetControlEnabled(string id, bool enabled)
    {
        if (_valueControls.TryGetValue(id, out var control))
        {
            control.IsEnabled = enabled;
        }

        if (_resetButtons.TryGetValue(id, out var resetButton) && SettingsRegistry.FindSetting(id) is { } definition)
        {
            resetButton.IsEnabled = enabled && SettingsViewModel.IsModifiedFromDefault(definition);
        }
    }

    private static bool IsControlEnabled(string id)
    {
        var settings = AppSettingsService.Current;
        return id switch
        {
            "AdjacentPreloadRadius" => settings.PreloadAdjacentImages,
            "HdrManualHeadroomStops" => settings.HdrHeadroomMode == HdrHeadroomMode.Manual,
            "SdrWhiteNits" => settings.SdrWhiteOverrideEnabled,
            "RtxVsrMode" => settings.RtxVsrMenuEnabled,
            "RtxVsrQualityPreset" => settings.RtxVsrMenuEnabled,
            "RtxVsrScaleFactor" => IsRtxVsrScaleFactorEnabled(settings),
            _ => true,
        };
    }

    /// <summary>
    /// The output scale factor only applies to the upscaling VSR modes; denoise and
    /// deblur operate at the source resolution and therefore have no scale choice.
    /// </summary>
    private static bool IsRtxVsrScaleFactorEnabled(AppUserSettings settings)
    {
        return settings.RtxVsrMenuEnabled
            && settings.RtxVsrMode is RtxVsrMode.Standard or RtxVsrMode.HighBitrate;
    }

    // ---- Setting value handlers -------------------------------------------------

    private void Toggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingControls || sender is not ToggleSwitch { Tag: SettingDefinition definition } toggle)
        {
            return;
        }

        _viewModel.SetValue(definition, toggle.IsOn);
        AfterSettingChangedFromUi(definition);
    }

    private void Choice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingControls || sender is not ComboBox { Tag: SettingDefinition definition } comboBox)
        {
            return;
        }

        if (comboBox.SelectedItem is ComboBoxItem { Tag: { } value })
        {
            _viewModel.SetValue(definition, value);
            AfterSettingChangedFromUi(definition);
        }
    }

    private void Number_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_isUpdatingControls || sender.Tag is not SettingDefinition definition)
        {
            return;
        }

        if (double.IsNaN(args.NewValue))
        {
            RefreshControlValue(definition);
            return;
        }

        var clamped = Math.Clamp(args.NewValue, definition.Minimum, definition.Maximum);
        object value;
        if (definition.DefaultValue is double)
        {
            value = clamped;
        }
        else
        {
            value = (int)Math.Round(clamped, MidpointRounding.AwayFromZero);
        }

        _viewModel.SetValue(definition, value);
        if (Math.Abs(clamped - args.NewValue) > NumberClampTolerance)
        {
            _isUpdatingControls = true;
            try
            {
                sender.Value = clamped;
            }
            finally
            {
                _isUpdatingControls = false;
            }
        }

        AfterSettingChangedFromUi(definition);
    }

    private void TextSetting_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingControls || sender is not TextBox { Tag: SettingDefinition definition } textBox)
        {
            return;
        }

        _viewModel.SetValue(definition, textBox.Text);
        AfterSettingChangedFromUi(definition);
    }

    // ---- Focus handling ---------------------------------------------------------

    /// <summary>
    /// A pointer press that no child control handled landed on a non-focusable part
    /// of the page (card background, labels, blank content). WinUI keeps keyboard
    /// focus in the previous TextBox/NumberBox in that case, so move it to the page
    /// itself; the focus change also commits a pending number/text edit. When the
    /// search results overlay is open, a press outside the result list dismisses it.
    /// </summary>
    private void SettingsPage_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (SearchResultsOverlay.Visibility == Visibility.Visible)
        {
            if (!IsPointerInsideSearchResults(e))
            {
                DismissSearch();
                QueueMoveFocusToPageContent();
            }

            return;
        }

        QueueMoveFocusToPageContent();
    }

    /// <summary>
    /// The release completes the pointer interaction (including XAML's own focus
    /// engagement and the focus clear that can happen when a clicked popup/overlay
    /// collapses), so this is the last chance to park focus on the page.
    /// </summary>
    private void SettingsPage_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        QueueMoveFocusToPageContent();
    }

    private bool IsPointerInsideSearchResults(PointerRoutedEventArgs e)
    {
        var position = e.GetCurrentPoint(SearchResultsList).Position;
        return position.X >= 0
            && position.Y >= 0
            && position.X <= SearchResultsList.ActualWidth
            && position.Y <= SearchResultsList.ActualHeight;
    }

    private void DismissSearch()
    {
        if (SearchBox.Text.Length > 0)
        {
            SearchBox.Text = string.Empty;
        }

        HideSearchResults();
    }

    private void MoveFocusToPageContent()
    {
        _ = MoveFocusToPageContentAsync();
    }

    /// <summary>
    /// Moves focus in a later dispatcher pass. XAML also reacts to a pointer press on
    /// non-focusable content by engaging the first focusable control, and that runs
    /// after the pointer handlers; a short timer makes the page the final focus
    /// target instead of whichever control XAML happened to engage.
    /// </summary>
    private void QueueMoveFocusToPageContent()
    {
        if (_pointerFocusTimer is null)
        {
            _pointerFocusTimer = DispatcherQueue.CreateTimer();
            _pointerFocusTimer.Interval = TimeSpan.FromMilliseconds(PointerFocusDelayMilliseconds);
            _pointerFocusTimer.IsRepeating = false;
            _pointerFocusTimer.Tick += PointerFocusTimer_Tick;
        }

        _pointerFocusTimer.Stop();
        _pointerFocusTimer.Start();
    }

    private void PointerFocusTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        MoveFocusToPageContent();
    }

    private async Task MoveFocusToPageContentAsync()
    {
        if (!await TryFocusAsync(this))
        {
            _ = await TryFocusAsync(ContentScroller);
        }
    }

    /// <summary>
    /// Queues focus for a setting that was just revealed by the search: the card is
    /// rebuilt synchronously, but it only becomes focusable after the next layout
    /// pass, so retry until the control/expander is live instead of dropping focus.
    /// </summary>
    private void QueueFocusAfterPageLayout(SettingDefinition definition)
    {
        DispatcherQueue.TryEnqueue(() => _ = FocusSettingAfterLayoutAsync(definition));
    }

    private async Task FocusSettingAfterLayoutAsync(SettingDefinition definition)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            if (await TryFocusSettingAsync(definition))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(40));
        }

        await MoveFocusToPageContentAsync();
    }

    private async Task<bool> TryFocusSettingAsync(SettingDefinition definition)
    {
        if (_valueControls.TryGetValue(definition.Id, out var control) && await TryFocusAsync(control))
        {
            return true;
        }

        return _cards.TryGetValue(definition.Id, out var card) && await TryFocusAsync(card);
    }

    private static async Task<bool> TryFocusAsync(DependencyObject target)
    {
        try
        {
            return (await FocusManager.TryFocusAsync(target, FocusState.Programmatic)).Succeeded;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Debug.WriteLine($"[SettingsPage] Failed to focus {target.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private void NumberSetting_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not NumberBox || !IsEditExitKey(e.Key))
        {
            return;
        }

        e.Handled = true;
        MoveFocusToPageContent();
    }

    private void TextSetting_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not TextBox || !IsEditExitKey(e.Key))
        {
            return;
        }

        e.Handled = true;
        MoveFocusToPageContent();
    }

    private static bool IsEditExitKey(VirtualKey key) => key is VirtualKey.Enter or VirtualKey.Escape;

    private void AfterSettingChangedFromUi(SettingDefinition definition)
    {
        UpdateResetButton(definition);
        UpdateDependentControlStates();
        if (string.Equals(definition.Id, "Language", StringComparison.Ordinal))
        {
            ShowLanguageRestartInfoBar();
        }
    }

    private void ViewModel_SettingChanged(object? sender, SettingChangedEventArgs e)
    {
        UpdateResetButton(e.Definition);
        if (string.Equals(e.Definition.Id, "Language", StringComparison.Ordinal))
        {
            ShowLanguageRestartInfoBar();
        }
    }

    private void ViewModel_SettingsReloaded(object? sender, EventArgs e)
    {
        RefreshAllControlValues();
        if (IsLanguageRestartPending())
        {
            ShowLanguageRestartInfoBar();
        }
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SettingDefinition definition })
        {
            return;
        }

        _viewModel.Reset(definition);
        RefreshControlValue(definition);
        if (string.Equals(definition.Id, "Language", StringComparison.Ordinal))
        {
            ShowLanguageRestartInfoBar();
        }
    }

    private async void ResetPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string pageId })
        {
            return;
        }

        if (!await ConfirmAsync("SettingsResetPageConfirmTitle", "SettingsResetPageConfirmMessage", "SettingsDialogConfirm"))
        {
            return;
        }

        _viewModel.ResetPage(pageId);
        RefreshAllControlValues();
        ShowOperationInfoBar(Loc("SettingsResetPageCompleted"), InfoBarSeverity.Success);
    }

    // ---- Button actions ---------------------------------------------------------

    private async void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SettingDefinition definition })
        {
            return;
        }

        try
        {
            switch (definition.Id)
            {
                case "OpenDefaultApps":
                    await OpenDefaultAppsAsync();
                    break;
                case "OpenConfigFolder":
                    OpenConfigFolder();
                    break;
                case "ExportSettings":
                    await ExportSettingsAsync();
                    break;
                case "ImportSettings":
                    await ImportSettingsAsync();
                    break;
                case "ResetAllSettings":
                    await ResetAllSettingsAsync();
                    break;
                default:
                    await _viewModel.ExecuteActionAsync(definition);
                    break;
            }
        }
        catch (Exception ex)
        {
            App.LogUnhandledUiException($"SettingsPage.ActionButton_Click.{definition.Id}", ex);
            ShowOperationInfoBar(Loc("SettingsActionFailed"), InfoBarSeverity.Error);
        }
    }

    private static async Task OpenDefaultAppsAsync()
    {
        try
        {
            _ = await Launcher.LaunchUriAsync(new Uri("ms-settings:defaultapps"));
        }
        catch (Exception ex)
        {
            App.LogUnhandledUiException("SettingsPage.OpenDefaultApps", ex);
        }
    }

    private static void OpenConfigFolder()
    {
        var path = AppSettingsService.SettingsDirectoryPath;
        Directory.CreateDirectory(path);
        _ = Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = "\"" + path + "\"",
            UseShellExecute = true,
        });
    }

    private async Task ExportSettingsAsync()
    {
        var picker = new FileSavePicker(GetMainWindowId())
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = "HDRImageViewer-settings",
            DefaultFileExtension = ".json",
        };
        picker.FileTypeChoices.Add(Loc("SettingsConfigFileType"), [".json"]);

        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        var json = SettingsViewModel.ExportToJson();
        await File.WriteAllTextAsync(file.Path, json);
        ShowOperationInfoBar(Loc("SettingsExportSucceeded", file.Path), InfoBarSeverity.Success);
    }

    private async Task ImportSettingsAsync()
    {
        var picker = new FileOpenPicker(GetMainWindowId())
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List,
        };
        picker.FileTypeFilter.Add(".json");

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        var json = await File.ReadAllTextAsync(file.Path);
        if (!await ConfirmAsync("SettingsImportConfirmTitle", "SettingsImportConfirmMessage", "SettingsDialogConfirm"))
        {
            return;
        }

        var result = _viewModel.ImportFromJson(json);
        if (result.Success)
        {
            ShowOperationInfoBar(Loc("SettingsImportSucceeded"), InfoBarSeverity.Success);
            if (IsLanguageRestartPending())
            {
                ShowLanguageRestartInfoBar();
            }

            return;
        }

        Debug.WriteLine($"[SettingsPage] Settings import failed: {result.ErrorDetail}");
        ShowOperationInfoBar(Loc("SettingsImportFailed"), InfoBarSeverity.Error);
    }

    private async Task ResetAllSettingsAsync()
    {
        if (!await ConfirmAsync("SettingsResetAllConfirmTitle", "SettingsResetAllConfirmMessage", "SettingsResetAllSettingsAction"))
        {
            return;
        }

        _viewModel.ResetAll();
        ShowOperationInfoBar(Loc("SettingsResetAllCompleted"), InfoBarSeverity.Success);
        if (IsLanguageRestartPending())
        {
            ShowLanguageRestartInfoBar();
        }
    }

    private async Task<bool> ConfirmAsync(string titleResourceKey, string messageResourceKey, string confirmResourceKey)
    {
        var dialog = new ContentDialog
        {
            Title = Loc(titleResourceKey),
            Content = new TextBlock
            {
                Text = Loc(messageResourceKey),
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = Loc(confirmResourceKey),
            CloseButtonText = Loc("SettingsDialogCancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private static WindowId GetMainWindowId()
    {
        return App.MainWindow?.AppWindow.Id
            ?? throw new InvalidOperationException(Loc("ErrorMainWindowNotInit"));
    }

    // ---- Info bar ---------------------------------------------------------------

    private void ShowLanguageRestartInfoBar()
    {
        SettingsInfoBar.Severity = InfoBarSeverity.Informational;
        SettingsInfoBar.Message = Loc("SettingsLanguageRestartTip.Text");
        RestartNowButton.Visibility = Visibility.Visible;
        SettingsInfoBar.IsOpen = true;
    }

    private void ShowOperationInfoBar(string message, InfoBarSeverity severity)
    {
        SettingsInfoBar.Severity = severity;
        SettingsInfoBar.Message = message;
        RestartNowButton.Visibility = Visibility.Collapsed;
        SettingsInfoBar.IsOpen = true;
    }

    private static bool IsLanguageRestartPending()
    {
        return !string.Equals(
            Localization.GetAppliedLanguageOverride() ?? string.Empty,
            AppSettingsService.Current.Language ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    private void RestartNowButton_Click(object sender, RoutedEventArgs e)
    {
        var currentFilePath = (App.MainWindow as MainWindow)?.CurrentOpenFilePath;
        var hasCurrentFile = !string.IsNullOrWhiteSpace(currentFilePath) && File.Exists(currentFilePath);
        var restartArguments = hasCurrentFile ? $"\"{currentFilePath}\"" : string.Empty;

        try
        {
            var failureReason = Microsoft.Windows.AppLifecycle.AppInstance.Restart(restartArguments);
            if (failureReason == Windows.ApplicationModel.Core.AppRestartFailureReason.RestartPending)
            {
                return;
            }
        }
        catch (Exception ex)
        {
            App.LogUnhandledUiException("SettingsPage.RestartNowButton_Click.AppInstanceRestart", ex);
        }

        try
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
            {
                App.UnregisterSingleInstance();
                var startInfo = new ProcessStartInfo(processPath)
                {
                    UseShellExecute = false,
                    WorkingDirectory = AppContext.BaseDirectory,
                };
                if (hasCurrentFile)
                {
                    startInfo.ArgumentList.Add(currentFilePath!);
                }

                _ = Process.Start(startInfo);
                App.MainWindow?.Close();
            }
        }
        catch (Exception ex)
        {
            App.LogUnhandledUiException("SettingsPage.RestartNowButton_Click.ProcessStart", ex);
        }
    }

    // ---- Search -----------------------------------------------------------------

    private void FocusSearchAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SearchBox.Focus(FocusState.Keyboard);
        SearchBox.SelectAll();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text;
        if (string.IsNullOrWhiteSpace(query))
        {
            HideSearchResults();
            return;
        }

        ShowSearchResults(query);
    }

    private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Enter:
                if (_searchResults.Count > 0)
                {
                    e.Handled = true;
                    ActivateSearchResult(_searchResults[0]);
                }

                break;
            case VirtualKey.Escape:
                e.Handled = true;
                DismissSearch();
                MoveFocusToPageContent();
                break;
            case VirtualKey.Down:
                if (_searchResults.Count > 0)
                {
                    e.Handled = true;
                    _ = SearchResultsList.Focus(FocusState.Keyboard);
                }

                break;
        }
    }

    private void ShowSearchResults(string query)
    {
        _searchResults.Clear();
        _searchResults.AddRange(SettingsRegistry.Search(query));
        SearchResultsList.Items.Clear();

        foreach (var result in _searchResults)
        {
            var item = new ListViewItem
            {
                Content = new TextBlock
                {
                    Text = result.Breadcrumb,
                    TextWrapping = TextWrapping.NoWrap,
                },
                Tag = result,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            AutomationProperties.SetAutomationId(item, "SettingsSearchResult" + result.Setting.Id);
            AutomationProperties.SetName(item, result.Breadcrumb);
            SearchResultsList.Items.Add(item);
        }

        SearchResultsHeading.Text = _searchResults.Count == 0
            ? Loc("SettingsSearchNoResults")
            : Loc("SettingsSearchResultsHeader", _searchResults.Count);
        SearchResultsOverlay.Visibility = Visibility.Visible;
    }

    private void HideSearchResults()
    {
        _searchResults.Clear();
        SearchResultsList.Items.Clear();
        SearchResultsOverlay.Visibility = Visibility.Collapsed;
    }

    private void SearchResultsList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ListViewItem { Tag: SettingsSearchResult result })
        {
            ActivateSearchResult(result);
        }
    }

    private void ActivateSearchResult(SettingsSearchResult result)
    {
        if (SearchBox.Text.Length > 0)
        {
            SearchBox.Text = string.Empty;
        }

        HideSearchResults();
        SelectPage(result.Page.Id);
        HighlightSetting(result.Setting);
        QueueFocusAfterPageLayout(result.Setting);
    }

    private void SelectPage(string pageId)
    {
        foreach (var menuItem in SettingsNavigation.MenuItems)
        {
            if (menuItem is NavigationViewItem { Tag: string id } item && string.Equals(id, pageId, StringComparison.Ordinal))
            {
                if (!ReferenceEquals(SettingsNavigation.SelectedItem, item))
                {
                    SettingsNavigation.SelectedItem = item;
                }
                else if (_currentPage is null || !string.Equals(_currentPage.Id, pageId, StringComparison.Ordinal))
                {
                    ShowPage(pageId);
                }

                return;
            }
        }
    }

    private void HighlightSetting(SettingDefinition definition)
    {
        if (_currentPage is null || !string.Equals(_currentPage.Id, definition.PageId, StringComparison.Ordinal))
        {
            ShowPage(definition.PageId);
        }

        if (!_cards.TryGetValue(definition.Id, out var card))
        {
            return;
        }

        // Settings on the advanced page live inside collapsed expanders; open the
        // one that owns the result before bringing the card into view and focusing it.
        if (_settingExpanders.TryGetValue(definition.Id, out var expander) && !expander.IsExpanded)
        {
            expander.IsExpanded = true;
        }

        card.StartBringIntoView(new BringIntoViewOptions
        {
            AnimationDesired = true,
            VerticalAlignmentRatio = 0.2,
        });

        if (card is not Control highlighted)
        {
            return;
        }

        highlighted.Background = new SolidColorBrush(Color.FromArgb(64, 0, 120, 215));
        _highlightedCard = highlighted;

        if (_highlightTimer is null)
        {
            _highlightTimer = DispatcherQueue.CreateTimer();
            _highlightTimer.Interval = TimeSpan.FromMilliseconds(HighlightDurationMilliseconds);
            _highlightTimer.IsRepeating = false;
            _highlightTimer.Tick += HighlightTimer_Tick;
        }

        _highlightTimer.Stop();
        _highlightTimer.Start();
    }

    private void HighlightTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        if (_highlightedCard is { } card)
        {
            card.ClearValue(Control.BackgroundProperty);
            _highlightedCard = null;
        }
    }
}
