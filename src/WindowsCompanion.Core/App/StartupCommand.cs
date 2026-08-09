namespace WindowsCompanion.Core.App;

public static class StartupCommand
{
    public const string StartupArgument = "--startup";

    public static string Build(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            throw new ArgumentException("Executable path is required.", nameof(executablePath));
        if (executablePath.Contains('"'))
            throw new ArgumentException("Executable path cannot contain a quote.", nameof(executablePath));

        return $"\"{executablePath}\" {StartupArgument}";
    }

    public static bool IsStartupLaunch(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return arguments.Any(argument =>
            string.Equals(argument, StartupArgument, StringComparison.OrdinalIgnoreCase));
    }
}
