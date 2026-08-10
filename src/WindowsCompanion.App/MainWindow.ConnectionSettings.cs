using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using WindowsCompanion.Core.App;
using WindowsCompanion.Core.Models;

namespace WindowsCompanion_App;

public sealed partial class MainWindow
{
    private List<string> _trustedSsids = [];
    private List<string> _trustedBssids = [];
    private bool _suppressSeparateUrlsToggle;
    private bool _suppressBssidToggle;

    private void OnShowConnection(object sender, RoutedEventArgs e)
    {
        LoadConnectionSettings();
        ShowView(View.Connection);
    }

    private void OnCloseConnection(object sender, RoutedEventArgs e)
    {
        RefreshStatusFields();
        ShowView(View.Status);
    }

    /// <summary>Fills the connection view from the saved settings.</summary>
    private void LoadConnectionSettings()
    {
        var settings = _controller.ConnectionSettings;
        SingleUrlBox.Text = settings.PrimaryUrl ?? _controller.BaseUrl ?? string.Empty;
        _suppressSeparateUrlsToggle = true;
        UseSeparateUrlsBox.IsChecked = settings.UseSeparateUrls;
        _suppressSeparateUrlsToggle = false;
        InternalUrlBox.Text = settings.InternalUrl ?? string.Empty;
        ExternalUrlBox.Text = settings.ExternalUrl ?? string.Empty;
        ConnectionModeBox.SelectedIndex = (int)settings.Mode;
        _trustedSsids = [.. settings.TrustedNetworks.Ssids];
        _trustedBssids = [.. settings.TrustedNetworks.Bssids];
        TrustedCidrsBox.Text = string.Join(Environment.NewLine, settings.TrustedNetworks.Cidrs);

        _suppressBssidToggle = true;
        RequireBssidBox.IsChecked = settings.TrustedNetworks.RequireBssidMatch;
        _suppressBssidToggle = false;

        TrustWiredBox.IsChecked = settings.TrustedNetworks.TrustWiredNetworks;
        ProbeUnknownBox.IsChecked = settings.TrustedNetworks.ProbeInternalOnUnknownNetworks;

        AcknowledgeUnreachableBox.IsChecked = false;
        AcknowledgeUnreachableBox.Visibility = Visibility.Collapsed;
        ConnectionResultText.Visibility = Visibility.Collapsed;
        SuggestionText.Text = string.Empty;

        UpdateSeparateUrlsVisibility();
        RefreshTrustedNetworkList();
        UpdateTrustedCidrValidation();
    }

    private void OnUseSeparateUrlsChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressSeparateUrlsToggle) return;
        UpdateSeparateUrlsVisibility();
        ConnectionResultText.Visibility = Visibility.Collapsed;
        AcknowledgeUnreachableBox.Visibility = Visibility.Collapsed;
    }

    private void UpdateSeparateUrlsVisibility()
    {
        var separate = UseSeparateUrlsBox.IsChecked == true;
        SingleUrlBox.Visibility = separate ? Visibility.Collapsed : Visibility.Visible;
        SeparateUrlsPanel.Visibility = separate ? Visibility.Visible : Visibility.Collapsed;
        TestRoutesButton.Content = separate ? "Test both URLs" : "Test URL";
    }

    private void RefreshTrustedNetworkList()
    {
        var network = _controller.CurrentNetwork;
        CurrentNetworkText.Text = network switch
        {
            { Kind: NetworkKind.Wireless, Ssid: { Length: > 0 } ssid } => $"Now on Wi-Fi “{ssid}”",
            { Kind: NetworkKind.Wireless } => "Now on Wi-Fi (Windows will not reveal the name)",
            { Kind: NetworkKind.Wired } => "Now on a wired network",
            { Kind: NetworkKind.Offline } => "Not connected to a network",
            _ => "Network type unknown"
        };
        TrustNetworkButton.IsEnabled = network is { Kind: NetworkKind.Wireless, Ssid: { Length: > 0 } }
                                       && !_trustedSsids.Contains(network.Ssid, StringComparer.Ordinal);

        TrustedNetworkList.Children.Clear();
        if (_trustedSsids.Count == 0)
        {
            TrustedNetworkList.Children.Add(new TextBlock
            {
                Text = "No trusted Wi-Fi networks yet.",
                FontSize = 12,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            });
            return;
        }

        foreach (var ssid in _trustedSsids.ToList())
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new TextBlock { Text = ssid, VerticalAlignment = VerticalAlignment.Center });
            var remove = new Button { Content = "Remove", Tag = ssid };
            remove.Click += OnRemoveTrustedNetwork;
            row.Children.Add(remove);
            TrustedNetworkList.Children.Add(row);
        }
    }

    private void OnTrustCurrentNetwork(object sender, RoutedEventArgs e)
    {
        var network = _controller.CurrentNetwork;
        if (network.Ssid is not { Length: > 0 } ssid) return;

        if (!_trustedSsids.Contains(ssid, StringComparer.Ordinal)) _trustedSsids.Add(ssid);

        // A BSSID is precise location data, so it is only ever recorded when the
        // user has asked for access-point matching.
        if (RequireBssidBox.IsChecked == true
            && network.Bssid is { Length: > 0 } bssid
            && !_trustedBssids.Contains(bssid, StringComparer.OrdinalIgnoreCase))
        {
            _trustedBssids.Add(bssid);
        }

        RefreshTrustedNetworkList();
    }

    /// <summary>Turning access-point matching off also discards what it recorded.</summary>
    private void OnRequireBssidChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressBssidToggle) return;

        if (RequireBssidBox.IsChecked == true)
        {
            OnTrustCurrentNetwork(sender, e);
            return;
        }

        _trustedBssids.Clear();
        RefreshTrustedNetworkList();
    }

    private void OnRemoveTrustedNetwork(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string ssid }) return;
        _trustedSsids.RemoveAll(s => string.Equals(s, ssid, StringComparison.Ordinal));
        // Access-point addresses are not tied to a single network name, so the only
        // safe moment to drop them is when no trusted network is left.
        if (_trustedSsids.Count == 0) _trustedBssids.Clear();
        RefreshTrustedNetworkList();
    }

    private void OnTrustedCidrsChanged(object sender, TextChangedEventArgs e) =>
        UpdateTrustedCidrValidation();

    private TrustedNetworkCidrValidation UpdateTrustedCidrValidation()
    {
        var validation = TrustedNetworkCidr.Validate(TrustedCidrEntries());
        var errorMessage = string.Join(
            Environment.NewLine,
            validation.Errors.Select(error =>
                $"Line {error.EntryNumber}: {error.Message}"));
        var errorChanged = !string.Equals(
            TrustedCidrsErrorText.Text,
            errorMessage,
            StringComparison.Ordinal);

        TrustedCidrsErrorText.Text = errorMessage;
        TrustedCidrsErrorText.Visibility = validation.IsValid
            ? Visibility.Collapsed
            : Visibility.Visible;
        AutomationProperties.SetHelpText(TrustedCidrsBox, errorMessage);

        if (!validation.IsValid && errorChanged)
        {
            var peer = FrameworkElementAutomationPeer.FromElement(TrustedCidrsErrorText)
                       ?? FrameworkElementAutomationPeer.CreatePeerForElement(TrustedCidrsErrorText);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
        else if (validation.IsValid && errorChanged)
        {
            var peer = FrameworkElementAutomationPeer.FromElement(TrustedCidrsBox)
                       ?? FrameworkElementAutomationPeer.CreatePeerForElement(TrustedCidrsBox);
            peer?.RaiseNotificationEvent(
                AutomationNotificationKind.ActionCompleted,
                AutomationNotificationProcessing.MostRecent,
                "Network CIDRs are valid.",
                "TrustedCidrsValidation");
        }

        return validation;
    }

    private IReadOnlyList<string> TrustedCidrEntries() =>
        (TrustedCidrsBox.Text ?? string.Empty)
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace('\r', '\n')
        .Split('\n', StringSplitOptions.TrimEntries);

    private ConnectionSettingsDraft BuildDraft() => new()
    {
        PrimaryUrl = SingleUrlBox.Text?.Trim(),
        UseSeparateUrls = UseSeparateUrlsBox.IsChecked == true,
        InternalUrl = UseSeparateUrlsBox.IsChecked == true ? InternalUrlBox.Text?.Trim() : null,
        ExternalUrl = UseSeparateUrlsBox.IsChecked == true ? ExternalUrlBox.Text?.Trim() : null,
        Mode = (ConnectionMode)Math.Max(0, ConnectionModeBox.SelectedIndex),
        AcknowledgeUnreachable = AcknowledgeUnreachableBox.IsChecked == true,
        TrustedNetworks = new TrustedNetworkSettings
        {
            Cidrs = [.. TrustedCidrEntries()],
            Ssids = [.. _trustedSsids],
            Bssids = [.. _trustedBssids],
            RequireBssidMatch = RequireBssidBox.IsChecked == true,
            TrustWiredNetworks = TrustWiredBox.IsChecked == true,
            ProbeInternalOnUnknownNetworks = ProbeUnknownBox.IsChecked == true
        }
    };

    private async void OnTestRoutes(object sender, RoutedEventArgs e)
    {
        SetConnectionBusy(true);
        try
        {
            ShowValidationReport(await _controller.TestConnectionSettingsAsync(BuildDraft()));
        }
        catch (Exception ex)
        {
            ShowConnectionResult(ex.Message, false);
        }
        finally
        {
            SetConnectionBusy(false);
        }
    }

    private async void OnSaveRoutes(object sender, RoutedEventArgs e)
    {
        SetConnectionBusy(true);
        try
        {
            var report = await _controller.SaveConnectionSettingsAsync(BuildDraft());
            ShowValidationReport(report);
            if (report.CanSave)
            {
                AcknowledgeUnreachableBox.Visibility = Visibility.Collapsed;
                RefreshStatusFields();
                return;
            }

            if (report.RequiresSignIn) await OfferReplaceServerAsync();
        }
        catch (Exception ex)
        {
            ShowConnectionResult(ex.Message, false);
        }
        finally
        {
            SetConnectionBusy(false);
        }
    }

    /// <summary>
    /// The addresses reach a different instance, so keeping the session is not an
    /// option. Replacing is destructive, so it is always an explicit choice.
    /// </summary>
    private async Task OfferReplaceServerAsync()
    {
        var url = UseSeparateUrlsBox.IsChecked != true
            ? SingleUrlBox.Text?.Trim()
            : ExternalUrlBox.Text?.Trim() is { Length: > 0 } external
            ? external
            : InternalUrlBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(url)) return;

        var replace = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Sign in to a different server?",
            Content = "The saved credentials are not valid at this address. Replacing the "
                      + "server revokes the current session and creates a new Mobile App "
                      + "device after browser sign-in.",
            PrimaryButtonText = "Replace and sign in",
            CloseButtonText = "Keep current server",
            DefaultButton = ContentDialogButton.Close
        };
        PrepareDialog(replace);

        if (await replace.ShowAsync() != ContentDialogResult.Primary) return;

        await _controller.RemoveServerAsync();
        try
        {
            await _controller.SignInAsync(url);
            _connected = true;
            DisconnectButton.Content = "Disconnect";
            UpdateNowButton.IsEnabled = true;
            _statusTimer.Start();
            ShowPanel(true);
            RefreshStatusFields();
        }
        catch (Exception ex)
        {
            _connected = false;
            ShowView(View.Connect);
            ShowConnectError(ex.Message);
        }
    }

    private void ShowValidationReport(RouteValidationReport report)
    {
        if (report.TrustedNetworkErrors is not null)
            UpdateTrustedCidrValidation();

        var lines = new List<string> { report.Summary };
        foreach (var entry in report.Entries)
        {
            var label = UseSeparateUrlsBox.IsChecked != true
                ? "Address"
                : entry.Route == RouteKind.Internal ? "Internal" : "External";
            lines.Add($"{label}: {entry.Describe()}");
        }

        AcknowledgeUnreachableBox.Visibility = report.RequiresAcknowledgement
            ? Visibility.Visible
            : AcknowledgeUnreachableBox.Visibility;

        ShowConnectionResult(string.Join(Environment.NewLine, lines), report.CanSave);
    }

    private void ShowConnectionResult(string message, bool positive)
    {
        ConnectionResultText.Text = message;
        ConnectionResultText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
            positive ? "SystemFillColorSuccessBrush" : "SystemFillColorCautionBrush"];
        ConnectionResultText.Visibility = Visibility.Visible;
    }

    private void SetConnectionBusy(bool busy)
    {
        ConnectionProgress.IsActive = busy;
        TestRoutesButton.IsEnabled = !busy;
        SaveRoutesButton.IsEnabled = !busy;
    }

    private async void OnSuggestUrls(object sender, RoutedEventArgs e)
    {
        var (internalUrl, externalUrl) = await _controller.SuggestedUrlsAsync();
        var found = new List<string>();

        if (!string.IsNullOrWhiteSpace(internalUrl) && string.IsNullOrWhiteSpace(InternalUrlBox.Text))
        {
            InternalUrlBox.Text = internalUrl;
            found.Add("internal");
        }

        if (!string.IsNullOrWhiteSpace(externalUrl) && string.IsNullOrWhiteSpace(ExternalUrlBox.Text))
        {
            ExternalUrlBox.Text = externalUrl;
            found.Add("external");
        }

        SuggestionText.Text = found.Count == 0
            ? "Home Assistant did not offer an address to fill in."
            : $"Filled in the {string.Join(" and ", found)} address; check it before saving.";
    }
}
