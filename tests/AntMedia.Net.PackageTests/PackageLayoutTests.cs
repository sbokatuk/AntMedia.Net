namespace AntMedia.Net.PackageTests;

/// <summary>
/// Asserts the shape of the produced NuGet packages. These run against the packed .nupkg rather
/// than the build output, so they catch packaging regressions the compiler cannot see — most
/// importantly a target framework going missing because one of the two SDK-band passes failed or
/// the merge step dropped it.
/// </summary>
public class PackageLayoutTests
{
    [Theory]
    [MemberData(nameof(Packages.AndroidFrameworks), MemberType = typeof(Packages))]
    public void Android_package_carries_a_binding_assembly_for_every_target_framework(string tfm)
    {
        using var package = Packages.OpenPackage(Packages.Android);

        var expected = $"lib/{tfm}/AntMedia.Net.Android.dll";
        Assert.True(
            package.GetEntry(expected) is not null,
            $"{Packages.Android} is missing '{expected}'.");
    }

    [Theory]
    [MemberData(nameof(Packages.AndroidFrameworks), MemberType = typeof(Packages))]
    public void Android_package_carries_the_native_aar_for_every_target_framework(string tfm)
    {
        using var package = Packages.OpenPackage(Packages.Android);

        var aar = package.GetEntry($"lib/{tfm}/webrtc-android-framework.aar");
        Assert.True(aar is not null, $"{Packages.Android} is missing the .aar for {tfm}.");

        // The framework vendors org.webrtc and jniLibs for four ABIs, so it is ~20 MB. Anything
        // small means a placeholder or a build that produced an empty archive.
        Assert.True(
            aar!.Length > 5_000_000,
            $"'{aar.FullName}' is only {aar.Length} bytes; the .aar looks empty.");
    }

    [SkippableTheory]
    [MemberData(nameof(ApplePackagesAndFrameworks))]
    public void Apple_packages_carry_a_binding_assembly_for_every_target_framework(
        string packageId, string tfm)
    {
        Skip.IfNot(Packages.Exists(packageId), $"{packageId} is only built on macOS");

        using var package = Packages.OpenPackage(packageId);

        var assembly = package.GetEntry($"lib/{tfm}/{packageId}.dll");
        Assert.True(assembly is not null, $"{packageId} is missing the assembly for {tfm}.");
    }

    public static IEnumerable<object[]> ApplePackagesAndFrameworks =>
        Packages.IosTargetFrameworks.Select(tfm => new object[] { Packages.IOS, tfm })
            .Concat(Packages.MacCatalystTargetFrameworks.Select(tfm => new object[] { Packages.Mac, tfm }));

    public static IEnumerable<object[]> ApplePackages =>
        new[] { Packages.IOS, Packages.Mac }.Select(id => new object[] { id });

    [SkippableTheory]
    [MemberData(nameof(ApplePackages))]
    public void Apple_packages_ship_one_copy_of_the_xcframeworks(string packageId)
    {
        Skip.IfNot(Packages.Exists(packageId), $"{packageId} is only built on macOS");

        // Both xcframeworks are dynamic, so they cannot be linked into the binding assembly: the
        // consuming app's linker would never see them and would fail on undefined
        // _OBJC_CLASS_$_AMSClient. They travel once per package under native/, and the
        // buildTransitive targets re-declare them in every consuming app - directly or
        // transitively. If the payload goes missing, every consumer breaks at link time, which no
        // other test here would catch.
        var names = Packages.NativePayload(packageId);

        Assert.True(
            names.Count > 0,
            $"{packageId} carries no native/ payload. Without it the frameworks never reach " +
            "the consuming app.");

        foreach (var framework in new[] { "WebRTCiOSSDK", "WebRTC" })
        {
            Assert.Contains(names, n => n.StartsWith($"{framework}.xcframework/", StringComparison.Ordinal));
        }
    }

    [SkippableTheory]
    [MemberData(nameof(ApplePackages))]
    public void Apple_packages_wire_the_payload_through_buildTransitive_targets(string packageId)
    {
        Skip.IfNot(Packages.Exists(packageId), $"{packageId} is only built on macOS");

        using var package = Packages.OpenPackage(packageId);

        // buildTransitive/ (not build/, which NuGet's default PrivateAssets hides from transitive
        // consumers) is what carries the NativeReferences to an app that arrives through
        // AntMedia.Net.Maui -> AntMedia.Net. The file must be named exactly <id>.targets or NuGet
        // never imports it, and it must point at both frameworks under native/ - the payload the
        // previous assertion proved is there.
        using var stream = Packages.ReadEntry(package, $"buildTransitive/{packageId}.targets");
        using var reader = new StreamReader(stream);
        var targets = reader.ReadToEnd();

        Assert.Contains("NativeReference", targets);
        Assert.Contains("native", targets);
        foreach (var framework in new[] { "WebRTCiOSSDK.xcframework", "WebRTC.xcframework" })
        {
            Assert.Contains(framework, targets);
        }
    }

    [SkippableTheory]
    [MemberData(nameof(ApplePackagesAndFrameworks))]
    public void Apple_lib_folders_carry_no_per_target_framework_payload(string packageId, string tfm)
    {
        Skip.IfNot(Packages.Exists(packageId), $"{packageId} is only built on macOS");

        using var package = Packages.OpenPackage(packageId);

        // The Apple SDK's default is to pack the NoBindingEmbedding sidecar into every
        // lib/<tfm>/, which is how the payload came to ship once per target framework. The
        // binding csprojs strip it by removing what the SDK's (private, renameable)
        // _IncludeBindingResourcesInNuGetPackage target adds - so if an SDK update renames that
        // seam, the sidecars quietly return, the package triples in size, and consumers link
        // every framework twice (once from the sidecar, once from the buildTransitive targets).
        // This is the assertion that turns that into a red pack validation instead.
        var sidecar = package.Entries
            .Where(e => e.FullName.StartsWith($"lib/{tfm}/{packageId}.resources", StringComparison.Ordinal))
            .Select(e => e.FullName)
            .ToList();

        Assert.True(
            sidecar.Count == 0,
            $"{packageId} ships a per-target-framework sidecar again for {tfm}: " +
            $"{string.Join(", ", sidecar.Take(3))}… The pack-time strip in the binding csproj " +
            "no longer matches the Apple SDK's packaging target.");
    }

    [SkippableFact]
    public void Ios_package_carries_both_a_device_and_a_simulator_slice()
    {
        Skip.IfNot(Packages.Exists(Packages.IOS), "the iOS package is only built on macOS");

        var names = Packages.NativePayload(Packages.IOS);

        // Both, or the package works in exactly one of the two places a developer will try it.
        Assert.Contains(names, n => n.Contains("/ios-arm64/", StringComparison.Ordinal));
        Assert.Contains(names, n => n.Contains("-simulator/", StringComparison.Ordinal));
    }

    [SkippableFact]
    public void Mac_package_carries_an_arm64_catalyst_slice_and_nothing_else()
    {
        Skip.IfNot(Packages.Exists(Packages.Mac), "the Mac package is only built on macOS");

        var slices = Packages.NativePayload(Packages.Mac)
            .Where(n => n.Contains(".xcframework/", StringComparison.Ordinal))
            .Select(n => n.Split(".xcframework/")[1].Split('/')[0])
            .Where(s => s.Length > 0 && !s.EndsWith(".plist", StringComparison.Ordinal))
            .Distinct()
            .ToList();

        // arm64 Catalyst only: Ant Media renders through OpenGL when arch != arm64 and Catalyst
        // has no OpenGL, and the iOS slices belong to AntMedia.Net.iOS. Anything else here is tens
        // of megabytes in every consuming app for something nothing can link against.
        Assert.All(slices, slice =>
            Assert.True(
                slice == "ios-arm64-maccatalyst",
                $"{Packages.Mac} ships the '{slice}' slice; it should carry ios-arm64-maccatalyst only."));
    }

    [SkippableTheory]
    [MemberData(nameof(ApplePackages))]
    public void Apple_frameworks_are_shallow_bundles(string packageId)
    {
        Skip.IfNot(Packages.Exists(packageId), $"{packageId} is only built on macOS");

        var names = Packages.NativePayload(packageId);
        Skip.If(names.Count == 0, "covered by the payload test");

        // The macOS versioned framework layout is mostly symlinks, and a NuGet package cannot
        // carry a symlink: each one becomes a real copy, the framework arrives with its content in
        // two places at once, and the consuming app fails at signing with "bundle format is
        // ambiguous (could be app or framework)". native/mac/flatten-frameworks.py is what keeps
        // the Catalyst slices shallow; this is the assertion that would catch its removal.
        Assert.DoesNotContain(names, n => n.Contains(".framework/Versions/", StringComparison.Ordinal));
    }

    public static IEnumerable<object[]> CrossPlatformPackagesAndFrameworks =>
        from packageId in new[] { Packages.Meta, Packages.Maui }
        from tfm in Packages.AndroidTargetFrameworks
            .Concat(Packages.IosTargetFrameworks)
            .Concat(Packages.MacCatalystTargetFrameworks)
        select new object[] { packageId, tfm };

    [SkippableTheory]
    [MemberData(nameof(CrossPlatformPackagesAndFrameworks))]
    public void Cross_platform_packages_carry_an_assembly_for_every_target_framework(
        string packageId, string tfm)
    {
        Skip.IfNot(Packages.Exists(packageId), $"{packageId} is only built on macOS");

        using var package = Packages.OpenPackage(packageId);

        // These span both platforms, so unlike the bindings they must be present for all six
        // target frameworks — a consumer multi-targeting Android and iOS resolves the same
        // package on both legs.
        var expected = $"lib/{tfm}/{packageId}.dll";
        Assert.True(
            package.GetEntry(expected) is not null,
            $"{packageId} is missing '{expected}'. It carries the cross-platform API, so every " +
            "target framework needs an assembly, not just a dependency group.");
    }

    [Theory]
    [MemberData(nameof(Packages.AndroidFrameworks), MemberType = typeof(Packages))]
    public void Android_package_ships_documentation_for_the_bound_api(string tfm)
    {
        using var package = Packages.OpenPackage(Packages.Android);

        using var xml = Packages.ReadEntry(package, $"lib/{tfm}/AntMedia.Net.Android.xml");
        var document = System.Xml.Linq.XDocument.Load(xml);
        var members = document.Descendants("member").Count();

        // The docs come from the SDK's own javadoc, via the sources jar that
        // native/android/fetch-android.sh builds. That jar is referenced conditionally so a
        // checkout without it still compiles — which means losing it would silently ship a
        // package documenting only the generated Resource class, as this one used to.
        Assert.True(
            members > 100,
            $"{Packages.Android} ({tfm}) documents only {members} members. The javadoc sources " +
            "jar was probably not built — see native/android/fetch-android.sh.");
    }

    [Fact]
    public void Packages_declare_the_expected_nuspec_metadata()
    {
        foreach (var id in new[]
                 {
                     Packages.Android, Packages.IOS, Packages.Mac, Packages.Meta, Packages.Maui,
                 })
        {
            if (!Packages.Exists(id))
            {
                continue;
            }

            using var package = Packages.OpenPackage(id);
            var nuspec = Packages.ReadNuspec(package, id);

            string Value(string name) => nuspec.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim() ?? string.Empty;

            Assert.Equal(id, Value("id"));
            Assert.NotEmpty(Value("version"));
            Assert.Equal("MIT", Value("license"));
            Assert.NotEmpty(Value("description"));
            Assert.Equal("icon.png", Value("icon"));
            Assert.Equal("README.md", Value("readme"));

            // Packed from the files the icon/readme metadata points at, so a rename that broke
            // the packaging would otherwise only show up on nuget.org.
            Assert.True(package.GetEntry("icon.png") is not null, $"{id} declares an icon it does not ship.");
            Assert.True(package.GetEntry("README.md") is not null, $"{id} declares a readme it does not ship.");
        }
    }
}
