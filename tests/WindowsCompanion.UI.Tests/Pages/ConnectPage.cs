using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;

namespace WindowsCompanion.UI.Tests.Pages;

internal sealed class ConnectPage(Window window)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    internal TextBox Url => Find("Connect.Url", ControlType.Edit).AsTextBox();
    internal Button SignIn => Find("Connect.SignIn", ControlType.Button).AsButton();

    internal void EnterUrl(string value)
    {
        AutomationWait.Until(
            () => Url.IsEnabled && SignIn.IsEnabled,
            "Connect controls did not become operable.");
        Url.Text = value;
        AutomationWait.Until(
            () => string.Equals(Url.Text, value, StringComparison.Ordinal),
            "The server URL was not accepted by the edit control.");
        SignIn.Invoke();
    }

    internal string WaitForError()
    {
        return Retry.WhileEmpty(
            () =>
            {
                var error = window.FindFirstDescendant(cf =>
                    cf.ByAutomationId("Connect.Error").And(cf.ByControlType(ControlType.Text)));
                return error is { IsOffscreen: false } ? error.Name : string.Empty;
            },
            timeout: Timeout,
            throwOnTimeout: true).Result
               ?? throw new TimeoutException("Connect error text did not become available.");
    }

    internal string? VisibleError
    {
        get
        {
            var error = window.FindFirstDescendant(cf =>
                cf.ByAutomationId("Connect.Error").And(cf.ByControlType(ControlType.Text)));
            return error is { IsOffscreen: false } ? error.Name : null;
        }
    }

    internal void WaitUntilVisible() =>
        AutomationWait.Until(
            () => window.FindFirstDescendant(cf =>
                cf.ByAutomationId("Connect.SignIn").And(cf.ByControlType(ControlType.Button)))
                is { IsOffscreen: false },
            "Connect page did not become visible.");

    private AutomationElement Find(string automationId, ControlType controlType) =>
        AutomationWait.Element(
            () => window.FindFirstDescendant(cf =>
                cf.ByAutomationId(automationId).And(cf.ByControlType(controlType))),
            automationId);
}

internal static class AutomationWait
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(40);
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);

    internal static AutomationElement Element(
        Func<AutomationElement?> find,
        string automationId) =>
        Retry.WhileNull(
            find,
            timeout: Timeout,
            interval: Interval,
            throwOnTimeout: true,
            ignoreException: true,
            timeoutMessage: $"Automation element '{automationId}' was not found.").Result
        ?? throw new TimeoutException($"Automation element '{automationId}' was not found.");

    internal static void Until(Func<bool> condition, string message) =>
        Retry.WhileFalse(
            condition,
            timeout: Timeout,
            interval: Interval,
            throwOnTimeout: true,
            timeoutMessage: message);
}
