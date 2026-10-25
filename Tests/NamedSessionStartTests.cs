using FocusDesk.Services;
using Xunit;

namespace FocusDesk.Tests;

/// <summary>Which named session a start runs on.</summary>
public class NamedSessionStartTests
{
    /// <summary>A session deleted while the focus-point window counts down would otherwise hand its
    /// start to the session in use, whose programs then hold for the whole length.</summary>
    [Fact]
    public void AStartNamingAMissingSession_IsRefused_WhileAStartNamingNoneRunsTheSessionInUse()
    {
        var settings = new AppSettings { NamedSessions = [NamedSession.Create("Writing"), NamedSession.Create("Reading")] };
        settings.SelectedNamedSession = settings.NamedSessions[1].Key;

        Assert.Null(NamedSessions.ForStart(settings, NamedSessions.NewKey()));
        Assert.Equal(settings.NamedSessions[1].Key, NamedSessions.ForStart(settings, key: null)!.Key);
    }
}
