using System.Collections.Immutable;
using System.Reflection.Metadata;
using GotoRef.Api.Domain.Models;

namespace GotoRef.Api.Parsers;

/// <summary>
/// Decodes type signatures into <see cref="TypeRef"/> records that carry package origin metadata,
/// enabling navigation between types across NuGet packages.
/// </summary>
internal sealed class RichTypeDecoder : ISignatureTypeProvider<TypeRef, object?>
{
    private readonly MetadataReader _reader;
    private readonly AssemblyPackageMap _map;

    public RichTypeDecoder(MetadataReader reader, AssemblyPackageMap map)
    {
        _reader = reader;
        _map = map;
    }

    public TypeRef GetPrimitiveType(PrimitiveTypeCode typeCode) => new(
        DisplayName: typeCode switch
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
            _                         => typeCode.ToString()
        },
        PackageId: null,
        PackageVersion: null,
        IsCurrentPackage: false
    );

    public TypeRef GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var def = reader.GetTypeDefinition(handle);
        var name = StripArity(reader.GetString(def.Name));
        var ns = reader.GetString(def.Namespace);
        return new TypeRef(
            DisplayName: name,
            PackageId: null,
            PackageVersion: null,
            IsCurrentPackage: true
        );
    }

    public TypeRef GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var typeRef = reader.GetTypeReference(handle);
        var name = StripArity(reader.GetString(typeRef.Name));
        var ns = reader.GetString(typeRef.Namespace);

        if (typeRef.ResolutionScope.Kind == HandleKind.AssemblyReference)
        {
            var asmRef = reader.GetAssemblyReference((AssemblyReferenceHandle)typeRef.ResolutionScope);
            var asmName = reader.GetString(asmRef.Name);

            if (_map.IsCurrentPackage(asmName))
                return new TypeRef(name, null, null, IsCurrentPackage: true);

            var mapping = _map.Resolve(asmName);
            if (mapping is not null)
                return new TypeRef(name, mapping.PackageId, mapping.Version, IsCurrentPackage: false);
        }
        else if (typeRef.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            // Nested type — resolve the enclosing type to get its assembly info
            var enclosing = GetTypeFromReference(reader, (TypeReferenceHandle)typeRef.ResolutionScope, rawTypeKind);
            return new TypeRef(name, enclosing.PackageId, enclosing.PackageVersion, enclosing.IsCurrentPackage);
        }

        // BCL or unknown — no navigable link
        return new TypeRef(name, null, null, IsCurrentPackage: false);
    }

    public TypeRef GetGenericInstantiation(TypeRef genericType, ImmutableArray<TypeRef> typeArguments)
    {
        var argNames = string.Join(", ", typeArguments.Select(a => a.DisplayName));
        return new TypeRef(
            DisplayName: $"{genericType.DisplayName}<{argNames}>",
            PackageId: genericType.PackageId,
            PackageVersion: genericType.PackageVersion,
            IsCurrentPackage: genericType.IsCurrentPackage,
            TypeArguments: typeArguments.ToArray()
        );
    }

    public TypeRef GetArrayType(TypeRef elementType, ArrayShape shape)
        => elementType with { DisplayName = $"{elementType.DisplayName}[]" };

    public TypeRef GetSZArrayType(TypeRef elementType)
        => elementType with { DisplayName = $"{elementType.DisplayName}[]" };

    public TypeRef GetByReferenceType(TypeRef elementType)
        => elementType with { DisplayName = $"ref {elementType.DisplayName}" };

    public TypeRef GetPointerType(TypeRef elementType)
        => elementType with { DisplayName = $"{elementType.DisplayName}*" };

    public TypeRef GetFunctionPointerType(MethodSignature<TypeRef> signature)
        => new("delegate*", null, null, false);

    public TypeRef GetGenericMethodParameter(object? genericContext, int index)
        => new($"T{index}", null, null, false);

    public TypeRef GetGenericTypeParameter(object? genericContext, int index)
        => new($"T{index}", null, null, false);

    public TypeRef GetModifiedType(TypeRef modifier, TypeRef unmodifiedType, bool isRequired)
        => unmodifiedType;

    public TypeRef GetPinnedType(TypeRef elementType)
        => elementType;

    public TypeRef GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    private static string StripArity(string name)
    {
        var backtick = name.IndexOf('`');
        return backtick >= 0 ? name[..backtick] : name;
    }
}
