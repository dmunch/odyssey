using System.Collections.Concurrent;
using System.Collections.Immutable;
using O9d.Guard;

namespace Odyssey;

/// <summary>
/// Delegate for resolving the CLR type of a previously stored event
/// </summary>
/// <param name="itemId">The stored event's item identifier</param>
/// <param name="metadata">The stored event's metadata</param>
/// <returns>Returns the CLR type to deserialized to, otherwise null if one could not be resolved</returns>
public delegate Type? TypeResolver(string itemId, Dictionary<string, object> metadata);

public static class TypeResolvers
{
    private static readonly ConcurrentDictionary<string, Type?> TypeCache = new();

    /// <summary>
    /// A type resolver that uses the _clr_type metadata item to resolve the type
    /// </summary>
    public static TypeResolver UsingClrQualifiedTypeMetadata => (itemId, metadata) =>
    {
        if (!metadata.TryGetValue(MetadataFields.ClrQualifiedType, out var clrTypeValue)
            || clrTypeValue is not string typeName)
        {
            throw new ArgumentException($"Item {itemId} is missing the required {MetadataFields.ClrQualifiedType} metadata value");
        }

        return TypeCache.GetOrAdd(typeName, key => Type.GetType(key));
    };

    /// <summary>
    /// Creates a type resolver that resolves the typed based on the _clr_type_name in the provided type map
    /// </summary>
    /// <param name="typeMap">Type map</param>
    /// <param name="fallbackType">An optional fallback type</param>
    /// <returns>The type resolver delegate</returns>
    public static TypeResolver UsingTypeMap(ImmutableDictionary<string, Type> typeMap, Type? fallbackType = null)
    {
        typeMap.NotNull();

        if (typeMap.Count == 0)
        {
            throw new ArgumentException("You must define at least one type mapping", nameof(typeMap));
        }

        return (itemId, metadata) =>
        {
            if (!metadata.TryGetValue(MetadataFields.ClrTypeName, out var clrTypeValue)
                || clrTypeValue is not string typeName)
            {
                throw new ArgumentException($"Item {itemId} is missing the required {MetadataFields.ClrTypeName} metadata value");
            }

            typeMap.TryGetValue(typeName, out Type? type);
            return type ?? fallbackType;
        };
    }
}