using System.Runtime.InteropServices;
using FocusDesk.Helpers;
using FocusDesk.Services;
using Xunit;

namespace FocusDesk.Tests;

/// <summary>A shortcut whose target is gone makes Explorer hold the shell call behind a box, which
/// stops the whole starting round, so the starter must recognise one before calling.</summary>
public class ProgramStarterTargetTests
{
    [Fact]
    public void A_shortcut_is_judged_gone_only_once_its_target_is_gone()
    {
        string folder = Directory.CreateTempSubdirectory("focusdesk-shortcut-").FullName;
        try
        {
            string target = Path.Combine(folder, "program.exe");
            File.WriteAllBytes(target, []);
            string shortcut = Path.Combine(folder, "program.lnk");
            Save(shortcut, target);

            Assert.False(ProgramStarter.ShortcutTargetGone(shortcut));
            File.Delete(target);
            Assert.True(ProgramStarter.ShortcutTargetGone(shortcut));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    private static void Save(string shortcut, string target)
    {
        var link = (ShellLink.IShellLinkW)(object)new ShellLink.ShellLinkObject();
        try
        {
            link.SetPath(target);
            ((ShellLink.IPersistFile)link).Save(shortcut, true);
        }
        finally { Marshal.ReleaseComObject(link); }
    }
}
