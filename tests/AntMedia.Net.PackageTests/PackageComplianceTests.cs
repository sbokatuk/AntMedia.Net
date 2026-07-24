using System.Xml.Linq;

namespace AntMedia.Net.PackageTests;

/// <summary>
/// Guards for package contents that restore and install cleanly when wrong and only fail much
/// later — a missing runtime dependency dies on device at first publish, missing symbol files
/// die in a consumer's debugger, and a missing licence notice fails no build at all.
/// </summary>
public class PackageComplianceTests
{
    /// <summary>
    /// gson is a runtime dependency the .aar does not bundle; without it a consumer crashes with
    /// NoClassDefFoundError at first publish, and the only other coverage is the live e2e, which
    /// is non-blocking. The groups travel through merge-packages.py's verbatim copy, so this
    /// pins what a lost group would silently drop.
    /// </summary>
    [SkippableTheory]
    [MemberData(nameof(Packages.AndroidFrameworks), MemberType = typeof(Packages))]
    public void Android_dependency_groups_carry_GoogleGson(string tfm)
    {
        Skip.IfNot(Packages.Exists(Packages.Android), "the Android package has not been packed");

        using var package = Packages.OpenPackage(Packages.Android);
        var nuspec = Packages.ReadNuspec(package, Packages.Android);

        var group = nuspec.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "group"
                && e.Attribute("targetFramework")?.Value == tfm);
        Assert.True(group is not null, $"{Packages.Android} has no dependency group for {tfm}.");

        var gson = group!.Elements()
            .Where(e => e.Name.LocalName == "dependency")
            .FirstOrDefault(e => e.Attribute("id")?.Value == "GoogleGson");

        Assert.True(
            gson is not null,
            $"{Packages.Android} group '{tfm}' does not depend on GoogleGson — consumers will " +
            "crash with NoClassDefFoundError on first publish.");
        Assert.False(
            string.IsNullOrEmpty(gson!.Attribute("version")?.Value),
            $"{Packages.Android} group '{tfm}' has a GoogleGson dependency with no version.");
    }

    /// <summary>
    /// The snupkg travels through the same two-pass merge as the nupkg; a merge bug would ship
    /// symbol packages missing the second band's pdbs with no signal until someone debugs.
    /// </summary>
    [SkippableTheory]
    [InlineData(Packages.Android)]
    [InlineData(Packages.IOS)]
    [InlineData(Packages.Mac)]
    [InlineData(Packages.Meta)]
    [InlineData(Packages.Maui)]
    public void Symbol_package_carries_a_pdb_for_every_target_framework(string packageId)
    {
        Skip.IfNot(Packages.Exists(packageId), $"{packageId} has not been packed");
        Skip.IfNot(
            Packages.Exists(packageId, ".snupkg"),
            $"{packageId} has no symbol package in the artifacts directory");

        // The frameworks the nupkg actually serves are the ones the snupkg must cover.
        List<string> frameworks;
        using (var package = Packages.OpenPackage(packageId))
        {
            frameworks = package.Entries
                .Select(e => e.FullName.Split('/'))
                .Where(parts => parts.Length == 3 && parts[0] == "lib" && parts[2].EndsWith(".dll"))
                .Select(parts => parts[1])
                .Distinct()
                .ToList();
        }

        Assert.NotEmpty(frameworks);

        using var symbols = Packages.OpenPackage(packageId, ".snupkg");
        var missing = frameworks
            .Where(tfm => symbols.GetEntry($"lib/{tfm}/{packageId}.pdb") is null)
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"{packageId}.snupkg has no pdb for: {string.Join(", ", missing)}.");
    }

    /// <summary>
    /// The packages redistribute BSD-licensed libwebrtc and the MIT Ant Media SDKs in binary
    /// form; both licences require their notice to travel with the binaries, and the MIT
    /// PackageLicenseExpression only covers this repository's own code.
    /// </summary>
    [SkippableTheory]
    [InlineData(Packages.Android)]
    [InlineData(Packages.IOS)]
    [InlineData(Packages.Mac)]
    [InlineData(Packages.Meta)]
    [InlineData(Packages.Maui)]
    public void Package_ships_third_party_notices(string packageId)
    {
        Skip.IfNot(Packages.Exists(packageId), $"{packageId} has not been packed");

        using var package = Packages.OpenPackage(packageId);
        Assert.True(
            package.GetEntry("THIRD-PARTY-NOTICES.txt") is not null,
            $"{packageId} does not ship THIRD-PARTY-NOTICES.txt.");
    }
}
