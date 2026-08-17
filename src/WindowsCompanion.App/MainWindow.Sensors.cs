using WindowsCompanion.Core.Lifecycle;
using WindowsCompanion.Core.Models;
using WindowsCompanion.Core.Sensors;
using WindowsCompanion_App.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace WindowsCompanion_App;

public sealed partial class MainWindow
{
    private int _sensorListBuildVersion;
    private bool _suppressSensorToggle;
    private readonly SensorPreviewCancellation _sensorPreviewCancellation = new();
    private readonly Dictionary<string, TextBlock> _sensorPreviewTexts =
        new(StringComparer.Ordinal);
    private readonly List<Control> _sensorSettingControls = [];
    private bool _loadingSensorSettings;
    private View _sensorReturnView = View.Status;

    private async void OnShowSensors(object sender, RoutedEventArgs e)
    {
        _sensorReturnView = View.Status;
        if (await BuildSensorListAsync())
            ShowView(View.Sensors);
    }

    private async void OnShowSensorsFromSettings(object sender, RoutedEventArgs e)
    {
        _sensorReturnView = View.Preferences;
        if (await BuildSensorListAsync())
            ShowView(View.Sensors);
    }

    private void OnCloseSensors(object sender, RoutedEventArgs e)
    {
        SensorSearchBox.Text = string.Empty;
        _sensorPreviewCancellation.CancelAll();
        RefreshPreferencesSummary();
        ShowView(_sensorReturnView);
    }

    private void OnSensorFilterChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        var filter = sender.Text?.Trim() ?? string.Empty;
        var anyVisible = false;

        foreach (var child in SensorList.Children)
        {
            if (child is Border border && border.Tag is string name)
            {
                var matches = filter.Length == 0
                    || name.Contains(filter, StringComparison.OrdinalIgnoreCase);
                border.Visibility = matches ? Visibility.Visible : Visibility.Collapsed;
                if (matches) anyVisible = true;
            }
        }

        SensorSearchEmpty.Visibility = anyVisible || filter.Length == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    /// <summary>
    /// Renders one toggle per catalog sensor. Built in code rather than bound so the
    /// list always reflects whatever sources the controller actually wired up.
    /// </summary>
    private async Task<bool> BuildSensorListAsync()
    {
        var catalog = _controller.Catalog;
        if (catalog is null) return false;

        var buildVersion = ++_sensorListBuildVersion;
        using var previewCancellation = _sensorPreviewCancellation.BeginList();
        IReadOnlyDictionary<string, string> previews;
        try
        {
            previews = await _controller.PreviewSensorsAsync(previewCancellation.Token);
        }
        catch (OperationCanceledException) when (previewCancellation.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            _sensorPreviewCancellation.EndList(previewCancellation);
        }

        if (buildVersion != _sensorListBuildVersion
            || !ReferenceEquals(catalog, _controller.Catalog)
            || (!_controller.IsDemoMode
                && _controller.State is ConnectionState.Disconnected or ConnectionState.AuthError))
        {
            return false;
        }

        SensorList.Children.Clear();
        SensorSearchBox.Text = string.Empty;
        SensorSearchEmpty.Visibility = Visibility.Collapsed;
        _sensorPreviewTexts.Clear();
        _sensorSettingControls.Clear();
        foreach (var definition in catalog.Definitions)
        {
            var toggle = new ToggleSwitch
            {
                IsOn = catalog.IsEnabled(definition.UniqueId),
                Tag = definition.UniqueId,
                OnContent = string.Empty,
                OffContent = string.Empty,
                Width = 48,
                MinWidth = 0,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(
                toggle,
                $"Sensors.Toggle.{definition.UniqueId}");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
                toggle,
                $"{definition.Name} enabled");
            toggle.Toggled += OnSensorToggled;
            _sensorSettingControls.Add(toggle);

            var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            heading.Children.Add(new TextBlock { Text = definition.Name, FontWeight = FontWeights.SemiBold });

            if (!string.IsNullOrWhiteSpace(definition.AutomationIdea))
            {
                var ideaText = $"Automation idea: {definition.AutomationIdea}";
                var idea = new Button
                {
                    Content = new FontIcon
                    {
                        Glyph = "\uE946",
                        FontSize = 12
                    },
                    Padding = new Thickness(5, 1, 5, 1),
                    MinWidth = 24,
                    MinHeight = 24,
                    UseSystemFocusVisuals = true,
                    VerticalAlignment = VerticalAlignment.Center,
                    Flyout = new Flyout
                    {
                        Content = AutomationIdeaText(ideaText)
                    }
                };
                ToolTipService.SetToolTip(idea, new ToolTip { Content = AutomationIdeaText(ideaText) });
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
                    idea,
                    $"Show automation idea for {definition.Name}");
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(idea, ideaText);
                heading.Children.Add(idea);
            }

            if (definition.Privacy == SensorPrivacy.Sensitive)
            {
                heading.Children.Add(new TextBlock
                {
                    Text = "sensitive",
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCautionBrush"]
                });
            }

            if (LifecycleSensorAdvisory.IsAdvisedSensor(definition.UniqueId))
            {
                // Says up front what the description spells out, so the caveat is
                // visible without reading the whole entry.
                heading.Children.Add(new TextBlock
                {
                    Text = LifecycleSensorAdvisory.Badge,
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCautionBrush"]
                });
            }

            var text = new StackPanel { Spacing = 2 };
            text.Children.Add(heading);
            text.Children.Add(new TextBlock
            {
                Text = definition.Description,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            });

            var metadata = new Grid
            {
                Margin = new Thickness(0, 8, 0, 0),
                ColumnSpacing = 12,
                RowSpacing = 4
            };
            metadata.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            metadata.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            var metadataRow = 0;
            if (!string.IsNullOrWhiteSpace(definition.ResourceUsage))
            {
                AddSensorMetadataRow(metadata, metadataRow++, "Impact", new TextBlock
                {
                    Text = definition.ResourceUsage,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
                        "TextFillColorSecondaryBrush"]
                });
            }
            var previewText = new TextBlock
            {
                Text = previews.TryGetValue(definition.UniqueId, out var value)
                    ? value
                    : definition.Privacy == SensorPrivacy.Sensitive
                      && !catalog.IsEnabled(definition.UniqueId)
                        ? "Read only once you enable this sensor"
                        : "Unavailable",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            };
            AddSensorMetadataRow(metadata, metadataRow, "Current value", previewText);
            text.Children.Add(metadata);
            _sensorPreviewTexts[definition.UniqueId] = previewText;

            if (definition.UniqueId == FrontmostAppSensorSource.FrontmostAppId)
                AddFrontmostAppDetailSetting(text, catalog);

            var row = new Grid
            {
                Padding = new Thickness(16, 14, 16, 14),
                ColumnSpacing = 16
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            Grid.SetColumn(text, 0);
            Grid.SetColumn(toggle, 1);
            row.Children.Add(text);
            row.Children.Add(toggle);

            SensorList.Children.Add(new Border
            {
                Tag = definition.Name,
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
                    "CardBackgroundFillColorDefaultBrush"],
                BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
                    "CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Child = row
            });
        }

        return true;
    }

    private static void AddSensorMetadataRow(
        Grid metadata,
        int row,
        string label,
        TextBlock value)
    {
        metadata.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var labelText = new TextBlock
        {
            Text = label,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
                "TextFillColorPrimaryBrush"]
        };
        Grid.SetRow(labelText, row);
        Grid.SetRow(value, row);
        Grid.SetColumn(value, 1);
        metadata.Children.Add(labelText);
        metadata.Children.Add(value);
    }

    private static TextBlock AutomationIdeaText(string text) => new()
    {
        Text = text,
        MaxWidth = 320,
        TextWrapping = TextWrapping.Wrap
    };

    private void AddFrontmostAppDetailSetting(StackPanel container, SensorCatalog catalog)
    {
        var mode = new ComboBox
        {
            Header = "Reported detail",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 6, 0, 0)
        };
        mode.Items.Add(new ComboBoxItem
        {
            Content = "Application name only",
            Tag = FrontmostAppMode.ApplicationName
        });
        mode.Items.Add(new ComboBoxItem
        {
            Content = "Full window title",
            Tag = FrontmostAppMode.FullWindowTitle
        });
        mode.SelectedIndex =
            catalog.Preferences.FrontmostAppMode == FrontmostAppMode.FullWindowTitle ? 1 : 0;
        mode.SelectionChanged += OnFrontmostAppModeChanged;
        _sensorSettingControls.Add(mode);

        container.Children.Add(mode);
        container.Children.Add(new TextBlock
        {
            Text = "Full titles may reveal document names, messages, customer names and websites.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
                "SystemFillColorCautionBrush"]
        });
    }

    private async void OnSensorToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressSensorToggle) return;
        if (sender is not ToggleSwitch { Tag: string uniqueId } toggle) return;

        var catalog = _controller.Catalog;
        if (catalog is null) return;

        if (LifecycleSensorAdvisory.RequiresConfirmation(uniqueId, toggle.IsOn, catalog.IsEnabled(uniqueId)))
        {
            var advisory = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = LifecycleSensorAdvisory.Title,
                Content = new TextBlock
                {
                    Text = LifecycleSensorAdvisory.Message,
                    TextWrapping = TextWrapping.Wrap
                },
                PrimaryButtonText = LifecycleSensorAdvisory.PrimaryButton,
                CloseButtonText = LifecycleSensorAdvisory.CloseButton,
                DefaultButton = ContentDialogButton.Close
            };
            PrepareDialog(advisory);

            toggle.IsEnabled = false;
            var answer = await advisory.ShowAsync();
            toggle.IsEnabled = true;

            if (answer != ContentDialogResult.Primary || !ReferenceEquals(catalog, _controller.Catalog))
            {
                // Nothing is saved or applied on a cancel: the toggle goes back to
                // where it was and the sensor stays off.
                SetToggleState(toggle, false);
                return;
            }
        }

        if (uniqueId == WinGetUpdateSensorSource.WinGetUpdatesId
            && toggle.IsOn
            && !catalog.IsEnabled(uniqueId))
        {
            toggle.IsEnabled = false;
            var capability = await _controller.ProbeWinGetCapabilityAsync();
            if (!ReferenceEquals(catalog, _controller.Catalog))
            {
                toggle.IsEnabled = true;
                SetToggleState(toggle, false);
                return;
            }

            if (!capability.IsReady)
            {
                capability = await ShowWinGetCapabilityDialogAsync(capability);
                toggle.IsEnabled = true;
                if (!capability.IsReady)
                {
                    SetToggleState(toggle, false);
                    return;
                }

                if (!ReferenceEquals(catalog, _controller.Catalog))
                {
                    SetToggleState(toggle, false);
                    return;
                }
            }

            toggle.IsEnabled = true;
        }

        var wasEnabled = catalog.IsEnabled(uniqueId);
        toggle.IsEnabled = false;
        using var previewCancellation = _sensorPreviewCancellation.BeginRow(uniqueId);
        Exception? refreshFailure = null;
        string? refreshedPreview = null;
        try
        {
            try
            {
                refreshedPreview = await catalog.SetEnabledAndRefreshAsync(
                    uniqueId,
                    toggle.IsOn,
                    previewCancellation.Token);
            }
            catch (OperationCanceledException) when (previewCancellation.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                if (catalog.IsEnabled(uniqueId) == toggle.IsOn)
                {
                    refreshFailure = ex;
                }
                else
                {
                    SetToggleState(toggle, wasEnabled);
                    ShowSensorPreviewError(uniqueId, "Could not update sensor: " + ex.Message);
                    return;
                }
            }

            if (!toggle.IsOn
                && _sensorPreviewTexts.TryGetValue(uniqueId, out var disabledPreview))
            {
                var definition = catalog.Definitions.First(candidate =>
                    string.Equals(candidate.UniqueId, uniqueId, StringComparison.Ordinal));
                disabledPreview.Text = definition.DisabledPreview;
                disabledPreview.Foreground =
                    (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
                        "TextFillColorSecondaryBrush"];
            }

            try
            {
                await _controller.ApplySensorChangesAsync();
            }
            catch (Exception ex)
            {
                catalog.SetEnabled(uniqueId, wasEnabled);
                SetToggleState(toggle, wasEnabled);
                ShowSensorPreviewError(uniqueId, "Could not update sensor: " + ex.Message);
                return;
            }

            if (!ReferenceEquals(catalog, _controller.Catalog)
                || previewCancellation.IsCancellationRequested)
            {
                return;
            }

            if (refreshFailure is not null)
            {
                ShowSensorPreviewError(uniqueId, "Refresh failed: " + refreshFailure.Message);
                return;
            }

            if (!ReferenceEquals(catalog, _controller.Catalog)
                || catalog.IsEnabled(uniqueId) != toggle.IsOn)
            {
                return;
            }

            if (_sensorPreviewTexts.TryGetValue(uniqueId, out var previewText))
            {
                previewText.Text = refreshedPreview ?? "Unavailable";
                previewText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
                    "TextFillColorSecondaryBrush"];
            }
        }
        catch (OperationCanceledException) when (previewCancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ShowSensorPreviewError(uniqueId, "Refresh failed: " + ex.Message);
        }
        finally
        {
            _sensorPreviewCancellation.EndRow(uniqueId, previewCancellation);
            if (ReferenceEquals(catalog, _controller.Catalog))
                toggle.IsEnabled = !_settingsActionBusy;
        }
    }

    private async Task<WinGetCapabilityResult> ShowWinGetCapabilityDialogAsync(
        WinGetCapabilityResult capability)
    {
        while (!capability.IsReady)
        {
            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(new TextBlock
            {
                Text = capability.Message,
                TextWrapping = TextWrapping.Wrap
            });

            if (capability.CanInstallOrRepair)
            {
                content.Children.Add(new TextBlock
                {
                    Text = "The companion will not install executable code automatically. "
                           + "Run the command below as the same Windows user, then return here "
                           + "and select Recheck.",
                    TextWrapping = TextWrapping.Wrap
                });
                content.Children.Add(new TextBox
                {
                    Header = "Run in PowerShell",
                    Text = PowerShellWinGetUpdateProvider.InstallCommand,
                    IsReadOnly = true,
                    TextWrapping = TextWrapping.Wrap
                });
            }

            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = "WinGet client module unavailable",
                Content = content,
                PrimaryButtonText = "Recheck",
                SecondaryButtonText = capability.CanInstallOrRepair ? "Copy command" : null,
                CloseButtonText = "Not now",
                DefaultButton = ContentDialogButton.Primary
            };

            var answer = await dialog.ShowAsync();
            if (answer == ContentDialogResult.None) return capability;
            if (answer == ContentDialogResult.Secondary)
            {
                var package = new DataPackage();
                package.SetText(PowerShellWinGetUpdateProvider.InstallCommand);
                Clipboard.SetContent(package);
                continue;
            }

            capability = await _controller.ProbeWinGetCapabilityAsync();
        }

        return capability;
    }

    private void ShowSensorPreviewError(string uniqueId, string message)
    {
        if (!_sensorPreviewTexts.TryGetValue(uniqueId, out var previewText)) return;

        previewText.Text = message;
        previewText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
            "SystemFillColorCautionBrush"];
    }

    private void SetToggleState(ToggleSwitch toggle, bool isOn)
    {
        _suppressSensorToggle = true;
        toggle.IsOn = isOn;
        _suppressSensorToggle = false;
    }

    private async void OnIdleMinutesChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loadingSensorSettings) return;
        var catalog = _controller.Catalog;
        if (catalog is null || double.IsNaN(args.NewValue)) return;

        catalog.Preferences.IdleThresholdSeconds = (int)Math.Max(1, args.NewValue) * 60;
        try
        {
            await _controller.ApplySensorChangesAsync();
            ShowSettingsActionStatus(
                _controller.IsDemoMode
                    ? "Idle threshold updated for this demo."
                    : "Idle threshold saved and synced.",
                true);
        }
        catch (Exception ex)
        {
            ShowSettingsActionStatus(
                "Could not save or sync the idle threshold: " + ex.Message,
                false);
        }
    }

    private async void OnFrontmostAppModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSensorSettings) return;
        if (sender is not ComboBox modeBox) return;

        var catalog = _controller.Catalog;
        if (catalog is null) return;

        var selected = modeBox.SelectedIndex == 1
            ? FrontmostAppMode.FullWindowTitle
            : FrontmostAppMode.ApplicationName;

        if (selected == FrontmostAppMode.FullWindowTitle
            && catalog.Preferences.FrontmostAppMode != FrontmostAppMode.FullWindowTitle)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = _controller.IsDemoMode
                    ? "Show full window titles locally?"
                    : "Share full window titles?",
                Content = "Window titles can contain document names, messages, customer names "
                          + (_controller.IsDemoMode
                              ? "and complete website titles. In demo mode, this value is shown "
                                + "only on this device and is not saved or sent."
                              : "and complete website titles. This value will be sent to your Home "
                                + "Assistant server whenever the sensor reports."),
                PrimaryButtonText = "Use full titles",
                CloseButtonText = "Keep application names",
                DefaultButton = ContentDialogButton.Close
            };
            PrepareDialog(dialog);

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                _loadingSensorSettings = true;
                modeBox.SelectedIndex = 0;
                _loadingSensorSettings = false;
                return;
            }
        }

        catalog.Preferences.FrontmostAppMode = selected;
        _controller.SaveSensorPreferences();
        await BuildSensorListAsync();
    }

}
