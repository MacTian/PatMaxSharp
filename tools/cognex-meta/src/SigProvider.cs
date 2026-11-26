#if COGNEX_META
using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace CognexMeta;

/// <summary>
/// Renders metadata signature blobs (types, methods, properties, fields) to C#-ish text.
/// Purely textual; no assembly loading, no type resolution, no dependency probing.
/// </summary>
public sealed class SigProvider : ISignatureTypeProvider<string, object?>, ICustomAttributeTypeProvider<string>
{
    private readonly MetadataReader _md;

    public SigProvider(MetadataReader md) => _md = md;

    // ---- primitives -------------------------------------------------------
    public string GetPrimitiveType(PrimitiveTypeCode code) => code switch
    {
        PrimitiveTypeCode.Boolean => "bool",
        PrimitiveTypeCode.Byte => "byte",
        PrimitiveTypeCode.Char => "char",
        PrimitiveTypeCode.Double => "double",
        PrimitiveTypeCode.Int16 => "short",
        PrimitiveTypeCode.Int32 => "int",
        PrimitiveTypeCode.Int64 => "long",
        PrimitiveTypeCode.IntPtr => "nint",
        PrimitiveTypeCode.Object => "object",
        PrimitiveTypeCode.SByte => "sbyte",
        PrimitiveTypeCode.Single => "float",
        PrimitiveTypeCode.String => "string",
        PrimitiveTypeCode.TypedReference => "TypedReference",
        PrimitiveTypeCode.UInt16 => "ushort",
        PrimitiveTypeCode.UInt32 => "uint",
        PrimitiveTypeCode.UInt64 => "ulong",
        PrimitiveTypeCode.UIntPtr => "nuint",
        PrimitiveTypeCode.Void => "void",
        _ => "?"
    };

    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        => TypeName(handle);

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var tr = reader.GetTypeReference(handle);
        string ns = reader.GetString(tr.Namespace);
        string name = reader.GetString(tr.Name);
        string full = string.IsNullOrEmpty(ns) ? name : ns + "." + name;
        var scope = tr.ResolutionScope;
        if (scope.Kind == HandleKind.TypeReference)
            full = GetTypeFromReference(reader, (TypeReferenceHandle)scope, rawTypeKind) + "+" + name;
        return StripArity(full);
    }

    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    public string GetSZArrayType(string elementType) => elementType + "[]";

    public string GetArrayType(string elementType, ArrayShape shape)
        => elementType + "[" + new string(',', Math.Max(0, shape.Rank - 1)) + "]";

    public string GetByReferenceType(string elementType) => "ref " + elementType;

    public string GetPointerType(string elementType) => elementType + "*";

    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
        => StripArity(genericType) + "<" + string.Join(", ", typeArguments) + ">";

    public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;

    public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;

    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired)
        => unmodifiedType + (isRequired ? " modreq(" : " modopt(") + modifier + ")";

    public string GetPinnedType(string elementType) => elementType + " pinned";

    public string GetFunctionPointerType(MethodSignature<string> signature)
        => "delegate*<" + string.Join(", ", signature.ParameterTypes) + ", " + signature.ReturnType + ">";

    // ---- ICustomAttributeTypeProvider ------------------------------------
    public string GetSystemType() => "System.Type";

    public string GetTypeFromSerializedName(string name) => name;

    public PrimitiveTypeCode GetUnderlyingEnumType(string type)
    {
        // Best effort: look the enum up by simple name among enum typedefs already parsed.
        return PrimitiveTypeCode.Int32;
    }

    public bool IsSystemType(string type) => type is "System.Type" or "Type";

    // ---- helpers ---------------------------------------------------------
    private static string StripArity(string name)
    {
        int tick = name.IndexOf('`');
        if (tick < 0) return name;
        int end = tick + 1;
        while (end < name.Length && char.IsDigit(name[end])) end++;
        return name.Substring(0, tick) + name.Substring(end);
    }

    public string TypeName(TypeDefinitionHandle handle)
    {
        var td = _md.GetTypeDefinition(handle);
        string name = _md.GetString(td.Name);
        var decl = td.GetDeclaringType();
        if (!decl.IsNil)
            return TypeName(decl) + "+" + StripArity(name);
        string ns = _md.GetString(td.Namespace);
        name = StripArity(name);
        if (string.IsNullOrEmpty(ns))
        {
            // Global-namespace private impl details: keep short but unique enough.
            return string.IsNullOrEmpty(name) ? "<Module>" : name;
        }
        return ns + "." + name;
    }

    /// <summary>Fully qualified name including nested chain, arity stripped.</summary>
    public string FullName(TypeDefinitionHandle handle) => TypeName(handle);

    public string HandleName(EntityHandle h) => h.Kind switch
    {
        HandleKind.TypeDefinition => TypeName((TypeDefinitionHandle)h),
        HandleKind.TypeReference => GetTypeFromReference(_md, (TypeReferenceHandle)h, 0),
        HandleKind.TypeSpecification => GetTypeFromSpecification(_md, null, (TypeSpecificationHandle)h, 0),
        _ => h.Kind.ToString()
    };

    /// <summary>Method name with declaring type prefix resolved when possible.</summary>
    public string MethodName(MethodDefinitionHandle h)
    {
        var m = _md.GetMethodDefinition(h);
        string name = _md.GetString(m.Name);
        var t = m.GetDeclaringType();
        if (t.IsNil) return name;
        return TypeName(t) + "::" + name;
    }

    public string FieldName(FieldDefinitionHandle h)
    {
        var f = _md.GetFieldDefinition(h);
        return TypeName(f.GetDeclaringType()) + "::" + _md.GetString(f.Name);
    }
}
#endif
