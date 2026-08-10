namespace WindowsCompanion_App.Services;

internal interface IMainWindowActivationTarget
{
    bool IsMinimized { get; }

    void Show();

    void Restore();

    void BringToFront();

    void Activate();
}

/// <summary>Applies the same idempotent activation sequence for every app entry point.</summary>
internal sealed class MainWindowActivation(IMainWindowActivationTarget target)
{
    internal void Activate()
    {
        target.Show();
        if (target.IsMinimized) target.Restore();
        target.BringToFront();
        target.Activate();
    }
}
