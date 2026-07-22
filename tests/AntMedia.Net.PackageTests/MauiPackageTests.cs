namespace AntMedia.Net.PackageTests;

/// <summary>
/// Asserts that AntMedia.Net.Maui ships a working video view for every platform it targets.
///
/// The handler's platform half is chosen with <c>#if</c> inside one file rather than by separate
/// files under Platforms/. That is deliberate — MAUI's SingleProject re-adds its own
/// Platforms/&lt;id&gt; globs after the project's items are evaluated, so a hand-rolled include for
/// Mac Catalyst was silently dropped — but it means a mistake in the conditions would compile
/// happily and produce a view bound to the wrong native type, or to none. These tests read the
/// packed assemblies and check what each one actually references.
/// </summary>
public class MauiPackageTests
{
    /// <summary>The native view the handler must be bound to on each platform.</summary>
    public static IEnumerable<object[]> ExpectedNativeView =>
        Packages.AndroidTargetFrameworks.Select(tfm => new object[] { tfm, "SurfaceViewRenderer" })
            .Concat(Packages.IosTargetFrameworks.Select(tfm => new object[] { tfm, "UIView" }))
            // Catalyst reuses the iOS half: UIKit is present there, so the view is real and lays
            // out, even though no video ever arrives.
            .Concat(Packages.MacCatalystTargetFrameworks.Select(tfm => new object[] { tfm, "UIView" }));

    [SkippableTheory]
    [MemberData(nameof(ExpectedNativeView))]
    public void Maui_package_exposes_the_view_and_its_handler(string tfm, string _)
    {
        Skip.IfNot(Packages.Exists(Packages.Maui), "the MAUI package is only built on macOS");

        using var package = Packages.OpenPackage(Packages.Maui);
        using var assembly = Packages.ReadEntry(package, $"lib/{tfm}/AntMedia.Net.Maui.dll");
        using var api = new AssemblyApi(assembly);

        foreach (var type in new[]
                 {
                     "AntMedia.Net.Maui.AntMediaVideoView",
                     "AntMedia.Net.Maui.AntMediaVideoViewHandler",
                     "AntMedia.Net.Maui.AppBuilderExtensions",
                     "AntMedia.Net.Maui.AntMediaClientExtensions",
                 })
        {
            Assert.Contains(type, api.PublicTypes);
        }
    }

    [SkippableTheory]
    [MemberData(nameof(ExpectedNativeView))]
    public void Maui_handler_is_bound_to_the_right_native_view(string tfm, string nativeView)
    {
        Skip.IfNot(Packages.Exists(Packages.Maui), "the MAUI package is only built on macOS");

        using var package = Packages.OpenPackage(Packages.Maui);
        using var assembly = Packages.ReadEntry(package, $"lib/{tfm}/AntMedia.Net.Maui.dll");
        using var api = new AssemblyApi(assembly);

        // The decisive check. Every platform's handler has the same name and members, so the only
        // evidence of which half was compiled is the native type it references. If the #if
        // conditions were wrong, this is what would catch it — the build would not.
        Assert.True(
            api.ReferencedTypes.Contains(nativeView),
            $"{Packages.Maui} ({tfm}) does not reference {nativeView}, so its handler was not " +
            "compiled from the expected platform half. Check the #if conditions in " +
            "AntMediaVideoView.cs.");
    }

    [SkippableTheory]
    [MemberData(nameof(ExpectedNativeView))]
    public void Maui_handler_is_not_bound_to_the_other_platform(string tfm, string nativeView)
    {
        Skip.IfNot(Packages.Exists(Packages.Maui), "the MAUI package is only built on macOS");

        var wrong = nativeView == "UIView" ? "SurfaceViewRenderer" : "UIView";

        using var package = Packages.OpenPackage(Packages.Maui);
        using var assembly = Packages.ReadEntry(package, $"lib/{tfm}/AntMedia.Net.Maui.dll");
        using var api = new AssemblyApi(assembly);

        Assert.True(
            !api.ReferencedTypes.Contains(wrong),
            $"{Packages.Maui} ({tfm}) references {wrong}, which belongs to the other platform.");
    }
}
