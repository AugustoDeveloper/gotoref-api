using System.Collections.Immutable;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security;
using System.Xml.Linq;
using GotoRef.Api.Domain.Models;

namespace GotoRef.Api.Parsers;

public class ReflectionParser : ILanguageParser
{
    public string Language => "dotnet";
    private readonly ILogger<ReflectionParser> _logger;

    public ReflectionParser(ILogger<ReflectionParser> logger) => _logger = logger;

    public async Task<List<NamespaceDetail>> ExtractTypesAsync(string nupkgPath, CancellationToken ct = default)
    {
        var extractDir = Path.Combine(Path.GetTempPath(), "gotoref-extracted", Path.GetFileNameWithoutExtension(nupkgPath));
        Directory.CreateDirectory(extractDir);

        await Task.Run(() => ExtractNupkg(nupkgPath, extractDir), ct);

        var dlls = FindBestDlls(extractDir);
        if (dlls.Count == 0) { _logger.LogWarning("No DLLs found in {Path}", nupkgPath); return []; }

        var xmlDocs = LoadXmlDocs(extractDir);
        var namespaces = new Dictionary<string, List<TypeDetail>>(StringComparer.Ordinal);
        var overloadCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var dll in dlls)
        {
            try { ExtractFromDll(dll, xmlDocs, namespaces, overloadCounts); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to read {Dll}", dll); }
        }

        return namespaces
            .OrderBy(kv => kv.Key)
            .Select(kv => new NamespaceDetail(kv.Key, kv.Value.OrderBy(t => t.Name).ToList()))
            .ToList();
    }

    public bool HasEmbeddedSource(string nupkgPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(nupkgPath);
            return zip.Entries.Any(e => e.FullName.StartsWith("src/", StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    public string? GetEmbeddedSource(string nupkgPath, string filePath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(nupkgPath);
            var normalizedRequest = filePath.Replace('\\', '/').TrimStart('/');
            var extractBase = Path.GetTempPath();

            foreach (var entry in zip.Entries)
            {
                var entryPath = entry.FullName.Replace('\\', '/');
                if (!entryPath.StartsWith("src/", StringComparison.OrdinalIgnoreCase)) continue;

                var fullPath = Path.GetFullPath(Path.Combine(extractBase, entry.FullName));
                if (!fullPath.StartsWith(extractBase, StringComparison.OrdinalIgnoreCase))
                    throw new SecurityException($"Path traversal detected: {entry.FullName}");

                var relative = entryPath["src/".Length..];
                if (!relative.Equals(normalizedRequest, StringComparison.OrdinalIgnoreCase)) continue;

                using var reader = new StreamReader(entry.Open());
                return reader.ReadToEnd();
            }
        }
        catch (SecurityException) { throw; }
        catch { }
        return null;
    }

    // ── Extraction ────────────────────────────────────────────────────────────

    private static void ExtractNupkg(string nupkgPath, string destDir)
    {
        if (Directory.EnumerateFiles(destDir, "*.dll", SearchOption.AllDirectories).Any()) return;

        var extractBase = Path.GetFullPath(destDir);
        using var zip = ZipFile.OpenRead(nupkgPath);

        foreach (var entry in zip.Entries)
        {
            var isLib = entry.FullName.StartsWith("lib/", StringComparison.OrdinalIgnoreCase);
            var isSrc = entry.FullName.StartsWith("src/", StringComparison.OrdinalIgnoreCase);
            if (!isLib && !isSrc) continue;
            if (entry.FullName.EndsWith('/')) continue;

            var dest = Path.GetFullPath(Path.Combine(destDir, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!dest.StartsWith(extractBase, StringComparison.OrdinalIgnoreCase))
                throw new SecurityException($"Path traversal detected: {entry.FullName}");

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, overwrite: true);
        }
    }

    private static List<string> FindBestDlls(string extractDir)
    {
        var libDir = Path.Combine(extractDir, "lib");
        if (!Directory.Exists(libDir)) return [];

        var preferred = new[] { "net8.0", "net7.0", "net6.0", "net5.0", "netstandard2.1", "netstandard2.0", "netstandard1.6", "net45" };
        var tfmDirs = Directory.GetDirectories(libDir);

        foreach (var tfm in preferred)
        {
            var match = tfmDirs.FirstOrDefault(d => Path.GetFileName(d).Equals(tfm, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return Directory.GetFiles(match, "*.dll").ToList();
        }

        var fallback = tfmDirs.FirstOrDefault();
        return fallback is not null ? Directory.GetFiles(fallback, "*.dll").ToList() : [];
    }

    // ── XML Docs ──────────────────────────────────────────────────────────────

    private static Dictionary<string, XmlDocMember> LoadXmlDocs(string extractDir)
    {
        var docs = new Dictionary<string, XmlDocMember>(StringComparer.Ordinal);
        foreach (var xmlFile in Directory.EnumerateFiles(extractDir, "*.xml", SearchOption.AllDirectories))
        {
            try
            {
                var doc = XDocument.Load(xmlFile);
                foreach (var member in doc.Descendants("member"))
                {
                    var name = member.Attribute("name")?.Value;
                    if (name is null) continue;
                    docs[name] = new XmlDocMember(
                        member.Element("summary")?.Value.Trim().NormalizeWs(),
                        member.Element("returns")?.Value.Trim().NormalizeWs(),
                        member.Elements("param").ToDictionary(
                            p => p.Attribute("name")?.Value ?? "",
                            p => p.Value.Trim().NormalizeWs())
                    );
                }
            }
            catch { }
        }
        return docs;
    }

    // ── DLL Parsing ───────────────────────────────────────────────────────────

    private static void ExtractFromDll(
        string dllPath,
        Dictionary<string, XmlDocMember> xmlDocs,
        Dictionary<string, List<TypeDetail>> namespaces,
        Dictionary<string, int> overloadCounts)
    {
        using var stream = File.OpenRead(dllPath);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata) return;

        var reader = peReader.GetMetadataReader();

        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeHandle);
            if (!typeDef.Attributes.HasFlag(TypeAttributes.Public) &&
                !typeDef.Attributes.HasFlag(TypeAttributes.NestedPublic)) continue;

            var typeName = reader.GetString(typeDef.Name);
            var namespaceName = reader.GetString(typeDef.Namespace);
            if (string.IsNullOrEmpty(namespaceName) || typeName.Contains('<') || typeName.Contains('`')) continue;

            var fullName = $"{namespaceName}.{typeName}";
            var kind = ResolveKind(reader, typeDef);
            xmlDocs.TryGetValue($"T:{fullName}", out var typeXml);

            var isStaticClass = typeDef.Attributes.HasFlag(TypeAttributes.Abstract) && typeDef.Attributes.HasFlag(TypeAttributes.Sealed);
            var isSealed = typeDef.Attributes.HasFlag(TypeAttributes.Sealed) && !typeDef.Attributes.HasFlag(TypeAttributes.Abstract);
            var isAbstract = typeDef.Attributes.HasFlag(TypeAttributes.Abstract) && !typeDef.Attributes.HasFlag(TypeAttributes.Sealed);

            var properties = ExtractProperties(reader, typeDef, fullName, xmlDocs);
            var (methods, ctors) = ExtractMethods(reader, typeDef, fullName, xmlDocs, overloadCounts);
            var (fields, enumValues) = ExtractFields(reader, typeDef, fullName, xmlDocs, kind);
            var events = ExtractEvents(reader, typeDef, fullName, xmlDocs);

            if (!namespaces.ContainsKey(namespaceName)) namespaces[namespaceName] = [];

            namespaces[namespaceName].Add(new TypeDetail(
                Name: typeName,
                Namespace: namespaceName,
                Kind: kind,
                IsStatic: isStaticClass,
                IsSealed: isSealed,
                IsAbstract: isAbstract,
                Summary: typeXml?.Summary,
                BaseType: GetBaseTypeName(reader, typeDef),
                Interfaces: GetInterfaceNames(reader, typeDef),
                GenericParameters: GetGenericParams(reader, typeDef.GetGenericParameters()),
                Properties: properties,
                Methods: methods,
                Constructors: ctors,
                Fields: fields,
                EnumValues: enumValues,
                Events: events
            ));
        }
    }

    private static string ResolveKind(MetadataReader reader, TypeDefinition typeDef)
    {
        if (typeDef.Attributes.HasFlag(TypeAttributes.Interface)) return "Interface";
        if (!typeDef.BaseType.IsNil && typeDef.BaseType.Kind == HandleKind.TypeReference)
        {
            var baseName = reader.GetString(reader.GetTypeReference((TypeReferenceHandle)typeDef.BaseType).Name);
            if (baseName == "Enum") return "Enum";
            if (baseName == "ValueType") return "Struct";
            if (baseName is "MulticastDelegate" or "Delegate") return "Delegate";
        }
        return "Class";
    }

    private static List<PropertyDetail> ExtractProperties(
        MetadataReader reader, TypeDefinition typeDef, string fullName,
        Dictionary<string, XmlDocMember> xmlDocs)
    {
        var result = new List<PropertyDetail>();
        foreach (var handle in typeDef.GetProperties())
        {
            var prop = reader.GetPropertyDefinition(handle);
            var name = reader.GetString(prop.Name);
            xmlDocs.TryGetValue($"P:{fullName}.{name}", out var xml);

            var accessors = prop.GetAccessors();
            if (accessors.Getter.IsNil) continue;
            var getter = reader.GetMethodDefinition(accessors.Getter);
            if (!getter.Attributes.HasFlag(MethodAttributes.Public)) continue;

            var hasGetter = !accessors.Getter.IsNil;
            var hasSetter = !accessors.Setter.IsNil;

            result.Add(new PropertyDetail(
                Name: name,
                TypeName: DecodePropertyType(reader, prop),
                HasGetter: hasGetter,
                HasSetter: hasSetter,
                Accessors: hasGetter && hasSetter ? "get; set;" : hasGetter ? "get;" : "set;",
                IsStatic: getter.Attributes.HasFlag(MethodAttributes.Static),
                Summary: xml?.Summary
            ));
        }
        return result;
    }

    private static (List<MethodDetail> Methods, List<MethodDetail> Constructors) ExtractMethods(
        MetadataReader reader, TypeDefinition typeDef, string fullName,
        Dictionary<string, XmlDocMember> xmlDocs, Dictionary<string, int> overloadCounts)
    {
        var methods = new List<MethodDetail>();
        var ctors = new List<MethodDetail>();

        foreach (var handle in typeDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(handle);
            if (!method.Attributes.HasFlag(MethodAttributes.Public)) continue;

            var name = reader.GetString(method.Name);
            if (name.StartsWith("get_") || name.StartsWith("set_") ||
                name.StartsWith("add_") || name.StartsWith("remove_") ||
                name.Contains('<')) continue;

            var isCtor = name is ".ctor" or ".cctor";
            var displayName = isCtor ? reader.GetString(typeDef.Name) : name;

            var overloadKey = $"{fullName}.{name}";
            overloadCounts.TryGetValue(overloadKey, out var overloadIdx);
            overloadCounts[overloadKey] = overloadIdx + 1;

            xmlDocs.TryGetValue($"M:{fullName}.{name}", out var xml);

            var isStatic = method.Attributes.HasFlag(MethodAttributes.Static);
            var isAbstract = method.Attributes.HasFlag(MethodAttributes.Abstract);
            var isVirtual = method.Attributes.HasFlag(MethodAttributes.Virtual) && !isAbstract;
            var isOverride = isVirtual && !method.Attributes.HasFlag(MethodAttributes.NewSlot);
            var genericParams = GetGenericParams(reader, method.GetGenericParameters());
            var (returnType, parameters) = DecodeMethodSignature(reader, method, xml);

            var modParts = new List<string> { "public" };
            if (isStatic) modParts.Add("static");
            if (isAbstract) modParts.Add("abstract");
            else if (isOverride) modParts.Add("override");
            else if (isVirtual) modParts.Add("virtual");

            var genericStr = genericParams.Length > 0 ? $"<{string.Join(", ", genericParams)}>" : "";
            var paramStr = string.Join(", ", parameters.Select(p => $"{p.TypeName} {p.Name}"));
            var signature = $"{string.Join(" ", modParts)} {returnType} {displayName}{genericStr}({paramStr})".Trim();

            var detail = new MethodDetail(
                Name: displayName,
                ReturnType: returnType,
                IsStatic: isStatic,
                IsAbstract: isAbstract,
                IsVirtual: isVirtual,
                IsOverride: isOverride,
                IsAsync: false,
                Signature: signature,
                Parameters: parameters,
                GenericParameters: genericParams,
                OverloadIndex: overloadIdx,
                Summary: xml?.Summary
            );

            if (isCtor) ctors.Add(detail);
            else methods.Add(detail);
        }
        return (methods, ctors);
    }

    private static (List<FieldDetail> Fields, List<FieldDetail> EnumValues) ExtractFields(
        MetadataReader reader, TypeDefinition typeDef, string fullName,
        Dictionary<string, XmlDocMember> xmlDocs, string kind)
    {
        var fields = new List<FieldDetail>();
        var enumValues = new List<FieldDetail>();

        foreach (var handle in typeDef.GetFields())
        {
            var field = reader.GetFieldDefinition(handle);
            if (!field.Attributes.HasFlag(FieldAttributes.Public)) continue;

            var name = reader.GetString(field.Name);
            if (name.Contains("__") || name.Contains('<')) continue;

            xmlDocs.TryGetValue($"F:{fullName}.{name}", out var xml);

            var detail = new FieldDetail(
                Name: name,
                TypeName: null,
                IsStatic: field.Attributes.HasFlag(FieldAttributes.Static),
                IsReadOnly: field.Attributes.HasFlag(FieldAttributes.InitOnly),
                IsConst: field.Attributes.HasFlag(FieldAttributes.Literal),
                Summary: xml?.Summary
            );

            if (kind == "Enum") enumValues.Add(detail);
            else fields.Add(detail);
        }
        return (fields, enumValues);
    }

    private static List<EventDetail> ExtractEvents(
        MetadataReader reader, TypeDefinition typeDef, string fullName,
        Dictionary<string, XmlDocMember> xmlDocs)
    {
        var result = new List<EventDetail>();
        foreach (var handle in typeDef.GetEvents())
        {
            var evt = reader.GetEventDefinition(handle);
            var name = reader.GetString(evt.Name);
            xmlDocs.TryGetValue($"E:{fullName}.{name}", out var xml);
            result.Add(new EventDetail(Name: name, TypeName: null, Summary: xml?.Summary));
        }
        return result;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string DecodePropertyType(MetadataReader reader, PropertyDefinition prop)
    {
        try { return prop.DecodeSignature(new SimpleTypeDecoder(), null).ReturnType; }
        catch { return "object"; }
    }

    private static (string ReturnType, List<ParameterDetail> Params) DecodeMethodSignature(
        MetadataReader reader, MethodDefinition method, XmlDocMember? xml)
    {
        try
        {
            var sig = method.DecodeSignature(new SimpleTypeDecoder(), null);
            var paramHandles = method.GetParameters().ToList();
            var parameters = new List<ParameterDetail>();

            for (int i = 0; i < sig.ParameterTypes.Length; i++)
            {
                var paramName = i < paramHandles.Count
                    ? reader.GetString(reader.GetParameter(paramHandles[i]).Name)
                    : $"arg{i}";
                if (xml is not null && xml.ParamDocs.TryGetValue(paramName, out var paramDoc))
                {
                    parameters.Add(new ParameterDetail(paramName, sig.ParameterTypes[i], false, null, paramDoc));
                }
            }
            return (sig.ReturnType, parameters);
        }
        catch { return ("void", []); }
    }

    private static string? GetBaseTypeName(MetadataReader reader, TypeDefinition typeDef)
    {
        if (typeDef.BaseType.IsNil || typeDef.BaseType.Kind != HandleKind.TypeReference) return null;
        try
        {
            var name = reader.GetString(reader.GetTypeReference((TypeReferenceHandle)typeDef.BaseType).Name);
            return name is "Object" or "ValueType" or "Enum" or "MulticastDelegate" or "Delegate" ? null : name;
        }
        catch { return null; }
    }

    private static string[] GetInterfaceNames(MetadataReader reader, TypeDefinition typeDef)
    {
        var names = new List<string>();
        foreach (var handle in typeDef.GetInterfaceImplementations())
        {
            try
            {
                var impl = reader.GetInterfaceImplementation(handle);
                if (impl.Interface.Kind == HandleKind.TypeReference)
                    names.Add(reader.GetString(reader.GetTypeReference((TypeReferenceHandle)impl.Interface).Name));
            }
            catch { }
        }
        return [.. names];
    }

    private static string[] GetGenericParams<T>(MetadataReader reader, GenericParameterHandleCollection handles)
        where T : struct
        => handles.Select(h => reader.GetString(reader.GetGenericParameter(h).Name)).ToArray();

    private static string[] GetGenericParams(MetadataReader reader, GenericParameterHandleCollection handles)
        => handles.Select(h => reader.GetString(reader.GetGenericParameter(h).Name)).ToArray();
}

internal record XmlDocMember(string? Summary, string? Returns, Dictionary<string, string> ParamDocs);

internal static class StringExtensions
{
    public static string NormalizeWs(this string s) =>
        System.Text.RegularExpressions.Regex.Replace(s.Trim(), @"\s+", " ");
}

internal class SimpleTypeDecoder : ISignatureTypeProvider<string, object?>
{
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
    {
        PrimitiveTypeCode.Void    => "void",
        PrimitiveTypeCode.Boolean => "bool",
        PrimitiveTypeCode.Byte    => "byte",
        PrimitiveTypeCode.SByte   => "sbyte",
        PrimitiveTypeCode.Char    => "char",
        PrimitiveTypeCode.Int16   => "short",
        PrimitiveTypeCode.UInt16  => "ushort",
        PrimitiveTypeCode.Int32   => "int",
        PrimitiveTypeCode.UInt32  => "uint",
        PrimitiveTypeCode.Int64   => "long",
        PrimitiveTypeCode.UInt64  => "ulong",
        PrimitiveTypeCode.Single  => "float",
        PrimitiveTypeCode.Double  => "double",
        PrimitiveTypeCode.String  => "string",
        PrimitiveTypeCode.Object  => "object",
        PrimitiveTypeCode.IntPtr  => "nint",
        PrimitiveTypeCode.UIntPtr => "nuint",
        _ => typeCode.ToString()
    };
    public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte _)
    {
        var def = r.GetTypeDefinition(h);
        return StripArity(r.GetString(def.Name));
    }
    public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte _)
    {
        return StripArity(r.GetString(r.GetTypeReference(h).Name));
    }
    public string GetGenericInstantiation(string g, ImmutableArray<string> args) => $"{g}<{string.Join(", ", args)}>";
    public string GetArrayType(string e, ArrayShape _) => $"{e}[]";
    public string GetSZArrayType(string e) => $"{e}[]";
    public string GetByReferenceType(string e) => $"ref {e}";
    public string GetPointerType(string e) => $"{e}*";
    public string GetFunctionPointerType(MethodSignature<string> _) => "delegate*";
    public string GetGenericMethodParameter(object? _, int i) => $"T{i}";
    public string GetGenericTypeParameter(object? _, int i) => $"T{i}";
    public string GetModifiedType(string m, string u, bool _) => u;
    public string GetPinnedType(string e) => e;
    public string GetTypeFromSpecification(MetadataReader r, object? ctx, TypeSpecificationHandle h, byte _)
        => r.GetTypeSpecification(h).DecodeSignature(this, ctx);
    private static string StripArity(string name)
    {
        var backtick = name.IndexOf('`');
        return backtick >= 0 ? name[..backtick] : name;
    }
}
