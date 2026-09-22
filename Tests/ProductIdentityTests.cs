using System.Reflection;
using Xunit;

namespace FocusDesk.Tests;

/// <summary>
/// The product name is a published interface, not a label: the installer's application folder, the
/// winget identifier, the update check's asset name and the firewall rule group all carry it, and a
/// rule left behind under one name cannot be removed under another. Pinned here so a rename has to
/// be a deliberate change rather than a typo that ships.
/// </summary>
public class ProductIdentityTests
{
    private static readonly Assembly Application = typeof(App).Assembly;

    [Fact]
    public void TheAssemblyIsNamedForTheProduct() =>
        Assert.Equal("FocusDesk", Application.GetName().Name);

    [Fact]
    public void TheProductAttributeCarriesTheProductName() =>
        Assert.Equal("FocusDesk", Application.GetCustomAttribute<AssemblyProductAttribute>()?.Product);

    [Fact]
    public void TheCompanyAttributeCarriesTheStudioName() =>
        Assert.Equal("ZeroZero Software", Application.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company);
}
