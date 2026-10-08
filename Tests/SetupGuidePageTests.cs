using FocusDesk.Helpers;
using Xunit;

namespace FocusDesk.Tests;

/// <summary>
/// The setup guide the Settings page opens. A page missing from the build output is a button that
/// opens an error, and nothing in the build notices.
/// </summary>
public class SetupGuidePageTests
{
    /// <summary>The test project references the application, so the application's copied content
    /// lands in this output as it lands beside the executable.</summary>
    [Fact]
    public void TheGuideReachesTheBuildOutput() =>
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, SetupGuidePage.RelativePath)),
            $"{SetupGuidePage.RelativePath} is not in the build output.");
}
