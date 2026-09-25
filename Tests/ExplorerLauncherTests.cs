using FocusDesk.Helpers;
using Xunit;

namespace FocusDesk.Tests;

/// <summary>
/// What Explorer is handed. Getting it wrong opens the wrong place, or nothing, and Explorer says
/// nothing about it either way.
/// </summary>
public class ExplorerLauncherTests
{
    [Fact]
    public void AnExistingFileIsSelected() =>
        Assert.Equal(@"/select,""C:\Users\Someone\AppData\Roaming\FocusDesk\settings.json""",
                     ExplorerLauncher.SelectFileArguments(
                         @"C:\Users\Someone\AppData\Roaming\FocusDesk\settings.json",
                         fileExists: true));

    /// <summary>A file nothing has written yet. Selecting it would open Explorer on nothing, so the
    /// folder is what opens — which is where the file will appear.</summary>
    [Fact]
    public void AMissingFileOpensItsFolder() =>
        Assert.Equal(@"""C:\Users\Someone\AppData\Roaming\FocusDesk""",
                     ExplorerLauncher.SelectFileArguments(
                         @"C:\Users\Someone\AppData\Roaming\FocusDesk\settings.json",
                         fileExists: false));
}
