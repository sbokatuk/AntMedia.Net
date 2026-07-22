using System.IO.Compression;
using System.Xml.Linq;

namespace AntMedia.Net.PackageTests;

/// <summary>
/// Locates the packed .nupkg files and describes what each package is expected to contain.
/// </summary>
public static class Packages
{
    public const string Android = "AntMedia.Net.Android";
    public const string IOS = "AntMedia.Net.iOS";
    public const string Mac = "AntMedia.Net.Mac";
    public const string Meta = "AntMedia.Net";
    public const string Maui = "AntMedia.Net.Maui";

    /// <summary>
    /// Target frameworks the Android package must carry, one per SDK band pass. These are pinned
    /// rather than discovered: a package that silently lost a target framework because a pack pass
    /// failed is exactly the regression these tests exist to catch.
    /// </summary>
    public static readonly string[] AndroidTargetFrameworks =
    [
        "net8.0-android34.0", "net9.0-android35.0", "net10.0-android36.0",
    ];

    public static readonly string[] IosTargetFrameworks =
    [
        "net8.0-ios18.0", "net9.0-ios18.0", "net10.0-ios26.0",
    ];

    /// <summary>
    /// Mac Catalyst, carried by the cross-platform packages and by AntMedia.Net.Mac. Same band
    /// split as iOS.
    /// </summary>
    public static readonly string[] MacCatalystTargetFrameworks =
    [
        "net8.0-maccatalyst18.0", "net9.0-maccatalyst18.0", "net10.0-maccatalyst26.0",
    ];

    public static IEnumerable<object[]> AndroidFrameworks =>
        AndroidTargetFrameworks.Select(tfm => new object[] { tfm });

    public static IEnumerable<object[]> IosFrameworks =>
        IosTargetFrameworks.Select(tfm => new object[] { tfm });

    public static IEnumerable<object[]> MacCatalystFrameworks =>
        MacCatalystTargetFrameworks.Select(tfm => new object[] { tfm });

    /// <summary>Every target framework the metapackage covers.</summary>
    public static IEnumerable<object[]> AllFrameworks =>
        AndroidTargetFrameworks.Concat(IosTargetFrameworks).Concat(MacCatalystTargetFrameworks)
            .Select(tfm => new object[] { tfm });

    /// <summary>
    /// The names of everything in a binding package's native payload for one target framework.
    ///
    /// Two shapes, both produced by the same NoBindingEmbedding setting: the .NET 9 SDK band
    /// writes a &lt;assembly&gt;.resources *directory*, the .NET 10 band writes a
    /// &lt;assembly&gt;.resources.zip. Both are unpacked and linked by the consuming app the same
    /// way, and both bands end up in the merged package, so a test that knew about only one of
    /// them would pass or fail depending on which band it happened to look at.
    ///
    /// Empty when neither is present, which is itself the interesting failure.
    /// </summary>
    public static IReadOnlyList<string> NativePayload(string packageId, string tfm)
    {
        using var package = OpenPackage(packageId);

        var directory = $"lib/{tfm}/{packageId}.resources/";
        var loose = package.Entries
            .Where(e => e.FullName.StartsWith(directory, StringComparison.Ordinal))
            .Select(e => e.FullName[directory.Length..])
            .ToList();

        if (loose.Count > 0)
        {
            return loose;
        }

        var zipped = package.GetEntry($"lib/{tfm}/{packageId}.resources.zip");
        if (zipped is null)
        {
            return [];
        }

        using var archive = new ZipArchive(zipped.Open());
        return archive.Entries.Select(e => e.FullName).ToList();
    }

    public static string ArtifactsDirectory { get; } = ResolveArtifactsDirectory();

    /// <summary>
    /// The Apple packages are only built on macOS, so on a Linux run the iOS, Mac and metapackage
    /// tests skip rather than fail. CI validates on a runner that has both sets downloaded.
    /// </summary>
    public static bool Exists(string packageId) => Find(packageId, throwIfMissing: false) is not null;

    public static string FindPackage(string packageId, string extension = ".nupkg") =>
        Find(packageId, throwIfMissing: true, extension)!;

    private static string? Find(string packageId, bool throwIfMissing, string extension = ".nupkg")
    {
        // The id is a filename prefix, and AntMedia.Net is a prefix of AntMedia.Net.Android, so
        // the version must be matched too or the metapackage lookup would find both.
        var matches = Directory.Exists(ArtifactsDirectory)
            ? Directory.GetFiles(ArtifactsDirectory, $"{packageId}.*{extension}")
                .Where(f => IsVersionOf(packageId, Path.GetFileName(f), extension))
                .ToArray()
            : [];

        if (matches.Length == 0)
        {
            Assert.True(
                !throwIfMissing,
                $"No {packageId}.<version>{extension} found in '{ArtifactsDirectory}'. " +
                "Run build/BuildNugets.sh (or the CI pack step) first.");
            return null;
        }

        // A rebuilt working copy can leave several versions behind; test the newest.
        return matches.OrderByDescending(File.GetLastWriteTimeUtc).First();
    }

    /// <summary>
    /// True when <paramref name="fileName" /> is "&lt;packageId&gt;.&lt;version&gt;&lt;extension&gt;"
    /// — that is, the remainder after the id starts a version rather than another id segment.
    /// </summary>
    private static bool IsVersionOf(string packageId, string fileName, string extension)
    {
        var remainder = fileName[(packageId.Length + 1)..^extension.Length];
        return remainder.Length > 0 && char.IsDigit(remainder[0]);
    }

    public static ZipArchive OpenPackage(string packageId, string extension = ".nupkg") =>
        ZipFile.OpenRead(FindPackage(packageId, extension));

    public static XDocument ReadNuspec(ZipArchive package, string packageId)
    {
        var entry = package.GetEntry($"{packageId}.nuspec");
        Assert.True(entry is not null, $"{packageId} has no .nuspec entry.");

        using var stream = entry!.Open();
        return XDocument.Load(stream);
    }

    /// <summary>Reads a package entry fully into memory so it can be seeked.</summary>
    public static MemoryStream ReadEntry(ZipArchive package, string entryName)
    {
        var entry = package.GetEntry(entryName);
        Assert.True(entry is not null, $"Package has no entry '{entryName}'.");

        var buffer = new MemoryStream();
        using (var stream = entry!.Open())
        {
            stream.CopyTo(buffer);
        }

        buffer.Position = 0;
        return buffer;
    }

    private static string ResolveArtifactsDirectory()
    {
        // Walk up to the repository root (the directory holding global.json).
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName ?? AppContext.BaseDirectory;

        // A relative ANTMEDIA_ARTIFACTS is resolved against the repository root, not the current
        // directory. The test process runs from bin/<config>/<tfm>, so the obvious-looking
        // ANTMEDIA_ARTIFACTS=artifacts would otherwise point at a directory inside the build
        // output and report every package as missing.
        return Environment.GetEnvironmentVariable("ANTMEDIA_ARTIFACTS") is { Length: > 0 } configured
            ? Path.GetFullPath(configured, root)
            : Path.Combine(root, "artifacts");
    }
}
