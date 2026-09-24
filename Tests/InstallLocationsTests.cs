using FocusDesk.Helpers;
using Xunit;

namespace FocusDesk.Tests;

/// <summary>Guards the check that stops the watchdog task registering against a build-output path
/// that will not exist once a development build is rebuilt or deleted.</summary>
public class InstallLocationsTests
{
    [Theory]
    [InlineData(@"C:\Users\Someone\AppData\Local\Programs\FocusDesk\FocusDesk.exe", true)]
    [InlineData(@"C:\repo\FocusDesk\bin\Debug\net10.0-windows10.0.26100.0\FocusDesk.exe", false)]
    [InlineData(@"C:\Users\Someone\AppData\Local\Programs\FocusDesk\SomethingElse.exe", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsInstalledExe_OnlyTrueForTheProductExeInTheProductFolder(string? exe, bool expected) =>
        Assert.Equal(expected, InstallLocations.IsInstalledExe(exe));
}
