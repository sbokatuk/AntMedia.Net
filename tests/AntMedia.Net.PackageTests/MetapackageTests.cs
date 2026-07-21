using System.Xml.Linq;

namespace AntMedia.Net.PackageTests;

/// <summary>
/// Asserts that the metapackage points every target framework at the right platform binding.
/// It carries no assemblies, so its dependency groups are the entire package — and they are
/// assembled from two SDK-band passes by build/merge-packages.py, which is exactly where a
/// target framework can go missing without anything failing.
/// </summary>
public class MetapackageTests
{
    private static readonly Dictionary<string, string> ExpectedDependency =
        Packages.AndroidTargetFrameworks.ToDictionary(tfm => tfm, _ => Packages.Android)
            .Concat(Packages.IosTargetFrameworks.ToDictionary(tfm => tfm, _ => Packages.IOS))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

    private static Dictionary<string, XElement> DependencyGroups()
    {
        using var package = Packages.OpenPackage(Packages.Meta);
        var nuspec = Packages.ReadNuspec(package, Packages.Meta);

        return nuspec.Descendants()
            .Where(e => e.Name.LocalName == "group" && e.Attribute("targetFramework") is not null)
            .ToDictionary(e => e.Attribute("targetFramework")!.Value, e => e);
    }

    [SkippableFact]
    public void Metapackage_covers_every_target_framework()
    {
        Skip.IfNot(Packages.Exists(Packages.Meta), "the metapackage is only built on macOS");

        var groups = DependencyGroups();
        var missing = ExpectedDependency.Keys.Except(groups.Keys).ToList();

        Assert.True(
            missing.Count == 0,
            $"{Packages.Meta} has no dependency group for: {string.Join(", ", missing)}. " +
            $"It declares {string.Join(", ", groups.Keys.Order())}.");
    }

    [SkippableFact]
    public void Metapackage_points_each_target_framework_at_its_platform_binding()
    {
        Skip.IfNot(Packages.Exists(Packages.Meta), "the metapackage is only built on macOS");

        var groups = DependencyGroups();

        foreach (var (tfm, expectedId) in ExpectedDependency)
        {
            Skip.IfNot(groups.ContainsKey(tfm), $"no group for {tfm}");

            var dependencies = groups[tfm]
                .Elements()
                .Where(e => e.Name.LocalName == "dependency")
                .Select(e => e.Attribute("id")?.Value)
                .ToList();

            // A group that lost its <dependency> children still restores cleanly and installs
            // nothing, which would look like the binding simply not working.
            Assert.True(
                dependencies.Contains(expectedId),
                $"{Packages.Meta} group '{tfm}' should depend on {expectedId} but depends on " +
                $"[{string.Join(", ", dependencies)}].");
        }
    }

    [SkippableFact]
    public void Metapackage_pins_its_dependencies_to_its_own_version()
    {
        Skip.IfNot(Packages.Exists(Packages.Meta), "the metapackage is only built on macOS");

        using var package = Packages.OpenPackage(Packages.Meta);
        var nuspec = Packages.ReadNuspec(package, Packages.Meta);

        var version = nuspec.Descendants()
            .First(e => e.Name.LocalName == "version").Value.Trim();

        foreach (var dependency in nuspec.Descendants().Where(e => e.Name.LocalName == "dependency"))
        {
            // Exact-version brackets: a floating range could pair a new facade with an old
            // binding, which fails at runtime rather than at restore.
            Assert.Equal($"[{version}]", dependency.Attribute("version")?.Value);
        }
    }
}
