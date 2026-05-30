using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using Microsoft.CodeAnalysis;

namespace Edict.Mcp.Versioning;

sealed class EdictVersionInspector
{
    const string EdictAssemblyPrefix = "Edict.";
    const string DllExtension = ".dll";
    const string AssemblyInformationalVersionAttributeFullName =
        "System.Reflection.AssemblyInformationalVersionAttribute";

    readonly string toolVersion;

    public EdictVersionInspector()
        : this(ResolveToolVersion())
    {
    }

    internal EdictVersionInspector(string toolVersion)
    {
        this.toolVersion = toolVersion;
    }

    public EdictVersionReport Inspect(Solution solution)
    {
        var aggregator = new EdictReferenceAggregator();
        foreach (var project in solution.Projects)
        {
            foreach (var reference in project.MetadataReferences)
            {
                if (reference is not PortableExecutableReference portableReference)
                {
                    continue;
                }
                var filePath = portableReference.FilePath;
                if (filePath is null || !IsEdictAssemblyPath(filePath))
                {
                    continue;
                }
                var probe = ReadAssemblyMetadata(filePath);
                if (probe is null)
                {
                    continue;
                }
                aggregator.Add(probe.Value.AssemblyName, probe.Value.InformationalVersion, project.Name);
            }
        }

        var references = aggregator.Build();
        var distinctVersions = references.Select(reference => reference.Version).Distinct().ToList();
        var hasNoEdictReferences = references.Count == 0;
        var hasInconsistentLibraryVersions = distinctVersions.Count > 1;
        var isDrifted = !hasNoEdictReferences && distinctVersions.Any(version => version != toolVersion);

        return new EdictVersionReport(
            ToolVersion: toolVersion,
            References: references,
            IsDrifted: isDrifted,
            HasNoEdictReferences: hasNoEdictReferences,
            HasInconsistentLibraryVersions: hasInconsistentLibraryVersions);
    }

    static bool IsEdictAssemblyPath(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        return fileName.StartsWith(EdictAssemblyPrefix, StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith(DllExtension, StringComparison.OrdinalIgnoreCase);
    }

    static (string AssemblyName, string InformationalVersion)? ReadAssemblyMetadata(string filePath)
    {
        try
        {
            using var fileStream = File.OpenRead(filePath);
            using var peReader = new PEReader(fileStream);
            if (!peReader.HasMetadata)
            {
                return null;
            }
            var metadataReader = peReader.GetMetadataReader();
            var assemblyDefinition = metadataReader.GetAssemblyDefinition();
            var assemblyName = metadataReader.GetString(assemblyDefinition.Name);
            var informationalVersion = ReadInformationalVersion(metadataReader, assemblyDefinition)
                ?? assemblyDefinition.Version.ToString();
            return (assemblyName, informationalVersion);
        }
        catch (Exception exception) when (exception is BadImageFormatException or IOException)
        {
            return null;
        }
    }

    static string? ReadInformationalVersion(MetadataReader metadataReader, AssemblyDefinition assemblyDefinition)
    {
        foreach (var attributeHandle in assemblyDefinition.GetCustomAttributes())
        {
            var attribute = metadataReader.GetCustomAttribute(attributeHandle);
            if (!IsInformationalVersionAttribute(metadataReader, attribute))
            {
                continue;
            }
            var blobReader = metadataReader.GetBlobReader(attribute.Value);
            if (blobReader.RemainingBytes < 2)
            {
                continue;
            }
            blobReader.ReadUInt16();
            return blobReader.ReadSerializedString();
        }
        return null;
    }

    static bool IsInformationalVersionAttribute(MetadataReader metadataReader, CustomAttribute attribute)
    {
        if (attribute.Constructor.Kind != HandleKind.MemberReference)
        {
            return false;
        }
        var memberReference = metadataReader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
        if (memberReference.Parent.Kind != HandleKind.TypeReference)
        {
            return false;
        }
        var typeReference = metadataReader.GetTypeReference((TypeReferenceHandle)memberReference.Parent);
        var typeNamespace = metadataReader.GetString(typeReference.Namespace);
        var typeName = metadataReader.GetString(typeReference.Name);
        var fullName = string.IsNullOrEmpty(typeNamespace) ? typeName : typeNamespace + "." + typeName;
        return fullName == AssemblyInformationalVersionAttributeFullName;
    }

    static string ResolveToolVersion()
    {
        var attribute = typeof(EdictVersionInspector).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        return attribute?.InformationalVersion ?? "unknown";
    }

    sealed class EdictReferenceAggregator
    {
        readonly SortedDictionary<(string AssemblyName, string Version), SortedSet<string>> projectsByReference
            = new();

        public void Add(string assemblyName, string version, string projectName)
        {
            var key = (assemblyName, version);
            if (!projectsByReference.TryGetValue(key, out var projects))
            {
                projects = new SortedSet<string>(StringComparer.Ordinal);
                projectsByReference[key] = projects;
            }
            projects.Add(projectName);
        }

        public IReadOnlyList<EdictVersionReference> Build()
        {
            return projectsByReference
                .Select(pair => new EdictVersionReference(
                    AssemblyName: pair.Key.AssemblyName,
                    Version: pair.Key.Version,
                    ProjectsReferencing: pair.Value.ToArray()))
                .ToArray();
        }
    }
}
