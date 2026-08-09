using WindowsCompanion.Core.Models;

namespace WindowsCompanion.Core.Abstractions;

/// <summary>Provides the current machine power/battery status from the OS.</summary>
public interface ISystemStatusProvider
{
    SystemStatus GetStatus();
}
