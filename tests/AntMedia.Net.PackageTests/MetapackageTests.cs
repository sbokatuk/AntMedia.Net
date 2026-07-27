using System.Xml.Linq;

namespace AntMedia.Net.PackageTests;

/// <summary>
/// Asserts that AntMedia.Net points every target framework at the right platform binding. The
/// package carries the cross-platform client assembly these days, but its dependency groups are
/// still what wires a consumer to the bindings — and they are assembled from two SDK-band passes
/// by build/merge-packages.py, which is exactly where a target framework can go missing without
/// anything failing.
/// </summary>
public class MetapackageTests
{
    /// <summary>
    /// Which binding each target framework must pull in. Catalyst takes AntMedia.Net.Mac rather
    /// than AntMedia.Net.iOS: the managed surface is the same, but the native payload underneath
    /// is a different build, and the iOS one has no Catalyst slice.
    /// </summary>
    private static readonly Dictionary<string, string?> ExpectedDependency =
        Packages.AndroidTargetFrameworks.ToDictionary(tfm => tfm, _ => (string?)Packages.Android)
            .Concat(Packages.IosTargetFrameworks.ToDictionary(tfm => tfm, _ => (string?)Packages.IOS))
            .Concat(Packages.MacCatalystTargetFrameworks.ToDictionary(tfm => tfm, _ => (string?)Packages.Mac))
            // The plain frameworks must resolve to *no* binding: they are what a Windows or console
            // consumer lands on, and asking one of those to install an Android .aar or an Apple
            // xcframework would trade a restore error for a stranger one.
            .Concat(Packages.FallbackTargetFrameworks.ToDictionary(tfm => tfm, _ => (string?)null))
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

            if (expectedId is null)
            {
                Assert.True(
                    dependencies.Count == 0,
                    $"{Packages.Meta} group '{tfm}' should carry no binding dependency but has " +
                    $"[{string.Join(", ", dependencies)}].");
                continue;
            }

            // A group that lost its <dependency> children still restores cleanly and installs
            // nothing, which would look like the binding simply not working.
            Assert.True(
                dependencies.Contains(expectedId),
                $"{Packages.Meta} group '{tfm}' should depend on {expectedId} but depends on " +
                $"[{string.Join(", ", dependencies)}].");
        }
    }

    /// <summary>
    /// The plain-framework assembly must be the one with no platform half in it. Compiling an
    /// Android or Apple half into it would not fail the build — the conditions in
    /// AntMedia.Net.csproj select by platform identifier, and getting them wrong yields an
    /// assembly that references a binding the plain dependency group does not install, which
    /// fails at load time in a consumer's app rather than here.
    /// </summary>
    [SkippableTheory]
    [MemberData(nameof(Packages.FallbackFrameworks), MemberType = typeof(Packages))]
    public void Metapackage_fallback_assembly_has_no_platform_behind_it(string tfm)
    {
        Skip.IfNot(Packages.Exists(Packages.Meta), "the metapackage is only built on macOS");

        using var package = Packages.OpenPackage(Packages.Meta);
        using var assembly = Packages.ReadEntry(package, $"lib/{tfm}/AntMedia.Net.dll");
        using var api = new AssemblyApi(assembly);

        Assert.Contains("AntMedia.Net.AntMediaClient", api.PublicTypes);

        // Positive evidence that Platforms/Unsupported was the half compiled: it is the only one
        // that refuses a session outright.
        Assert.True(
            api.ReferencedTypes.Contains("PlatformNotSupportedException"),
            $"{Packages.Meta} ({tfm}) never throws PlatformNotSupportedException, so it was not " +
            "built from Platforms/Unsupported.");

        foreach (var native in new[] { "WebRTCClientBuilder", "AMSClient", "UIView", "SurfaceViewRenderer" })
        {
            Assert.True(
                !api.ReferencedTypes.Contains(native),
                $"{Packages.Meta} ({tfm}) references {native}, which belongs to a platform half. " +
                "The plain frameworks pull in no binding, so that type will not be there at runtime.");
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
