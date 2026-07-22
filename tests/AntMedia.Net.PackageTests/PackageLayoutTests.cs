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
    [MemberData(nameof(Packages.IosFrameworks), MemberType = typeof(Packages))]
    public void Ios_package_carries_a_binding_assembly_for_every_target_framework(string tfm)
    {
        Skip.IfNot(Packages.Exists(Packages.IOS), "the iOS package is only built on macOS");

        using var package = Packages.OpenPackage(Packages.IOS);

        var assembly = package.GetEntry($"lib/{tfm}/AntMedia.Net.iOS.dll");
        Assert.True(assembly is not null, $"{Packages.IOS} is missing the assembly for {tfm}.");
    }

    [SkippableFact]
    public void Ios_package_ships_the_xcframeworks_and_wires_them_up()
    {
        Skip.IfNot(Packages.Exists(Packages.IOS), "the iOS package is only built on macOS");

        using var package = Packages.OpenPackage(Packages.IOS);

        // Both xcframeworks are dynamic, so they cannot be embedded in the binding assembly: the
        // consuming app's linker would never see them and would fail on undefined
        // _OBJC_CLASS_$_AMSClient. They ship as package content and build/*.targets declares them
        // as NativeReference in the consuming project — if either half goes missing, every
        // consumer breaks at link time, which no other test here would catch.
        foreach (var framework in new[] { "WebRTCiOSSDK", "WebRTC" })
        {
            var slices = package.Entries
                .Where(e => e.FullName.StartsWith($"native/{framework}.xcframework/", StringComparison.Ordinal))
                .ToList();

            Assert.True(slices.Count > 0, $"{Packages.IOS} does not ship native/{framework}.xcframework.");

            // Device and simulator slices both have to be there, or the package works in exactly
            // one of the two places a developer will try it.
            Assert.Contains(slices, e => e.FullName.Contains("/ios-arm64/", StringComparison.Ordinal));
            Assert.Contains(slices, e => e.FullName.Contains("-simulator/", StringComparison.Ordinal));
        }

        var targets = package.GetEntry($"build/{Packages.IOS}.targets");
        Assert.True(targets is not null, $"{Packages.IOS} is missing build/{Packages.IOS}.targets.");

        using var reader = new StreamReader(targets!.Open());
        var content = reader.ReadToEnd();

        Assert.Contains("NativeReference", content);
        Assert.Contains("WebRTCiOSSDK.xcframework", content);
        Assert.Contains("WebRTC.xcframework", content);
    }

    public static IEnumerable<object[]> CrossPlatformPackagesAndFrameworks =>
        from packageId in new[] { Packages.Meta, Packages.Maui }
        from tfm in Packages.AndroidTargetFrameworks.Concat(Packages.IosTargetFrameworks)
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

    [Fact]
    public void Packages_declare_the_expected_nuspec_metadata()
    {
        foreach (var id in new[] { Packages.Android, Packages.IOS, Packages.Meta, Packages.Maui })
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
