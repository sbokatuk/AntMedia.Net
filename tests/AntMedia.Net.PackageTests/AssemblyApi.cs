using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace AntMedia.Net.PackageTests;

/// <summary>
/// Reads the public API out of a binding assembly using metadata only. The assemblies target
/// *-android and *-ios and reference Mono.Android / Microsoft.iOS, so they cannot be loaded into
/// the test process; the metadata reader lets these tests run on a plain desktop runner with no
/// emulator, simulator or workload installed.
/// </summary>
public sealed class AssemblyApi : IDisposable
{
    private readonly PEReader _peReader;
    private readonly MetadataReader _metadata;
    private IReadOnlyList<string>? _publicTypes;

    public AssemblyApi(Stream assembly)
    {
        _peReader = new PEReader(assembly);
        _metadata = _peReader.GetMetadataReader();
    }

    /// <summary>Namespace-qualified names of every public top-level type.</summary>
    public IReadOnlyList<string> PublicTypes => _publicTypes ??= _metadata.TypeDefinitions
        .Select(_metadata.GetTypeDefinition)
        .Where(type => (type.Attributes & TypeAttributes.VisibilityMask) == TypeAttributes.Public)
        .Select(FullNameOf)
        .ToList();

    public IReadOnlyList<string> MethodsOf(string typeFullName)
    {
        var type = FindType(typeFullName);
        return type.GetMethods()
            .Select(_metadata.GetMethodDefinition)
            .Where(method => (method.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public)
            .Select(method => _metadata.GetString(method.Name))
            .ToList();
    }

    /// <summary>
    /// Every type this assembly references from elsewhere, by simple name.
    ///
    /// Used to prove which platform an assembly was actually compiled for: a handler bound to
    /// <c>SurfaceViewRenderer</c> and one bound to <c>UIView</c> have identical type names and
    /// members, and differ only in what they reference.
    /// </summary>
    public IReadOnlyList<string> ReferencedTypes => _metadata.TypeReferences
        .Select(_metadata.GetTypeReference)
        .Select(reference => _metadata.GetString(reference.Name))
        .Distinct()
        .ToList();

    public IReadOnlyList<string> PropertiesOf(string typeFullName)
    {
        var type = FindType(typeFullName);
        return type.GetProperties()
            .Select(_metadata.GetPropertyDefinition)
            .Select(property => _metadata.GetString(property.Name))
            .ToList();
    }

    private TypeDefinition FindType(string typeFullName)
    {
        foreach (var handle in _metadata.TypeDefinitions)
        {
            var type = _metadata.GetTypeDefinition(handle);
            if (FullNameOf(type) == typeFullName)
            {
                return type;
            }
        }

        throw new InvalidOperationException($"Type '{typeFullName}' is not defined in this assembly.");
    }

    private string FullNameOf(TypeDefinition type)
    {
        var name = _metadata.GetString(type.Name);
        var ns = _metadata.GetString(type.Namespace);
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }

    public void Dispose() => _peReader.Dispose();
}
