using WindowsCompanion.Core.App;

namespace WindowsCompanion.Core.Tests;

public class StartupCommandTests
{
    [Fact]
    public void Build_quotes_the_executable_and_adds_startup_argument()
    {
        Assert.Equal(
            "\"C:\\Program Files\\WindowsCompanion\\WindowsCompanion.exe\" --startup",
            StartupCommand.Build("C:\\Program Files\\WindowsCompanion\\WindowsCompanion.exe"));
    }

    [Theory]
    [InlineData("--startup", true)]
    [InlineData("--STARTUP", true)]
    [InlineData("--other", false)]
    public void Detects_startup_launch(string argument, bool expected)
    {
        Assert.Equal(expected, StartupCommand.IsStartupLaunch([argument]));
    }
}
