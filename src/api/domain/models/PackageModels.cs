namespace GotoRef.Api.Domain.Models;

public record PackageSearchResult(
    string Id,
    string Version,
    string Description,
    string? Authors,
    long TotalDownloads,
    string? IconUrl,
    string? ProjectUrl,
    string[] Tags
);

public record PackageVersionsResult(
    string Id,
    string[] Versions
);

public record PackageRegistryMetadata(
    long TotalDownloads,
    string[] TargetFrameworks,
    string? LicenseExpression,
    string? LicenseUrl,
    string? ProjectUrl,
    string? RepositoryUrl,
    string? RepositoryType,
    string? CommitHash,
    DateTimeOffset? PublishedAt
);

public record PackageDetail(
    string Id,
    string Version,
    PackageMetadata Metadata,
    int TotalTypes,
    bool HasEmbeddedSource,
    List<NamespaceDetail> Namespaces
);

public record PackageMetadata(
    string? LicenseUrl,
    string? ProjectUrl,
    RepositoryInfo? Repository,
    DateTimeOffset? PublishedAt
);

public record RepositoryInfo(
    string? Url,
    string? Type,
    string? Commit
);

public record NamespaceDetail(
    string Namespace,
    List<TypeDetail> Types
);

public record TypeDetail(
    string Name,
    string Namespace,
    string Kind,             // Class | Struct | Interface | Enum | Record | Delegate
    bool IsStatic,
    bool IsSealed,
    bool IsAbstract,
    string? Summary,
    string? BaseType,
    string[] Interfaces,
    string[] GenericParameters,
    List<PropertyDetail> Properties,
    List<MethodDetail> Methods,
    List<MethodDetail> Constructors,
    List<FieldDetail> Fields,
    List<FieldDetail> EnumValues,
    List<EventDetail> Events,
    TypeRef? BaseTypeRef = null,
    TypeRef[]? InterfacesRef = null
);

public record PropertyDetail(
    string Name,
    string TypeName,
    bool HasGetter,
    bool HasSetter,
    string Accessors,        // "get;" | "get; set;" | "get; init;"
    bool IsStatic,
    string? Summary,
    TypeRef? TypeNameRef = null
);

public record MethodDetail(
    string Name,
    string ReturnType,
    bool IsStatic,
    bool IsAbstract,
    bool IsVirtual,
    bool IsOverride,
    bool IsAsync,
    string Signature,
    List<ParameterDetail> Parameters,
    string[] GenericParameters,
    int OverloadIndex,
    string? Summary,
    TypeRef? ReturnTypeRef = null
);

public record ParameterDetail(
    string Name,
    string TypeName,
    bool IsOptional,
    string? DefaultValue,
    string? XmlDoc,
    TypeRef? TypeNameRef = null
);

public record FieldDetail(
    string Name,
    string? TypeName,
    bool IsStatic,
    bool IsReadOnly,
    bool IsConst,
    string? Summary,
    TypeRef? TypeNameRef = null
);

public record EventDetail(
    string Name,
    string? TypeName,
    string? Summary,
    TypeRef? TypeNameRef = null
);

public record TypeRef(
    string DisplayName,
    string? PackageId,
    string? PackageVersion,
    bool IsCurrentPackage,
    TypeRef[]? TypeArguments = null
);
