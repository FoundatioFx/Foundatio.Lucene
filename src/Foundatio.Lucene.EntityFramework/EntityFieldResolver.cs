using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Foundatio.Lucene.EntityFramework;

/// <summary>
/// Resolves dotted field paths against the EF Core model on demand. Each entity or complex type's members are read
/// once (after the configured filters) and cached per parser, so discovery cost is proportional to the paths queries
/// actually use rather than to the size of the navigation graph.
/// </summary>
internal sealed class EntityFieldResolver
{
    private const int MaxCachedPathsPerType = 4096;

    private readonly EntityFrameworkQueryParserConfiguration _configuration;
    private readonly ConditionalWeakTable<ITypeBase, Dictionary<string, EntityFieldInfo>> _members = new();
    private readonly ConditionalWeakTable<ITypeBase, ConcurrentDictionary<string, EntityFieldInfo>> _paths = new();

    public EntityFieldResolver(EntityFrameworkQueryParserConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// Resolves a path such as <c>Company.Address.City</c> relative to <paramref name="root"/>, returning null when it
    /// does not name a discovered member.
    /// </summary>
    public EntityFieldInfo? Resolve(ITypeBase root, string path)
    {
        if (path.Length == 0)
            return null;

        var cache = _paths.GetValue(root, static _ => new ConcurrentDictionary<string, EntityFieldInfo>(StringComparer.OrdinalIgnoreCase));
        if (cache.TryGetValue(path, out var cached))
            return cached;

        var resolved = ResolveCore(root, path);
        if (resolved is not null && cache.Count < MaxCachedPathsPerType)
            resolved = cache.GetOrAdd(path, resolved);

        return resolved;
    }

    private EntityFieldInfo? ResolveCore(ITypeBase root, string path)
    {
        var type = root;
        EntityFieldInfo? current = null;
        int hops = 0;
        int start = 0;

        while (start <= path.Length)
        {
            int dot = path.IndexOf('.', start);
            string segment = dot < 0 ? path[start..] : path[start..dot];
            if (segment.Length == 0 || type is null)
                return null;

            if (!GetMembers(type).TryGetValue(segment, out var member))
                return null;

            if (member.TargetType is not null && ++hops > _configuration.MaxFieldDepth && dot >= 0)
                return null;

            string fullName = current is null ? member.Name : current.FullName + "." + member.Name;
            current = new EntityFieldInfo
            {
                Name = member.Name,
                FullName = fullName,
                ClrType = member.ClrType,
                ElementType = member.ElementType,
                Kind = member.Kind,
                Metadata = member.Metadata,
                TargetType = member.TargetType,
                Parent = current,
                IsFullTextSearch = member.Kind == EntityFieldKind.Property && IsFullTextField(member, fullName)
            };

            if (dot < 0)
                return current;

            type = member.TargetType;
            start = dot + 1;
        }

        return null;
    }

    private bool IsFullTextField(EntityFieldInfo member, string fullName)
    {
        var fields = _configuration.FullTextFields;
        if (fields.Count == 0 || member.ValueType != typeof(string) || member.IsCollection)
            return false;

        return fields.Contains(fullName)
            || member.Metadata?.DeclaringType is { } declaringType && fields.Contains(declaringType.ClrType.Name + "." + member.Name);
    }

    private Dictionary<string, EntityFieldInfo> GetMembers(ITypeBase type)
    {
        return _members.GetValue(type, DiscoverMembers);
    }

    private Dictionary<string, EntityFieldInfo> DiscoverMembers(ITypeBase type)
    {
        var members = new Dictionary<string, EntityFieldInfo>(StringComparer.OrdinalIgnoreCase);
        var config = _configuration;

        foreach (var property in type.GetProperties())
        {
            if (config.PropertyFilter?.Invoke(property) == false)
                continue;

            members.TryAdd(property.Name, new EntityFieldInfo
            {
                Name = property.Name,
                ClrType = property.ClrType,
                ElementType = property.IsPrimitiveCollection ? property.GetElementType()?.ClrType : null,
                Kind = EntityFieldKind.Property,
                Metadata = property
            });
        }

        foreach (var complexProperty in type.GetComplexProperties())
        {
            if (config.ComplexPropertyFilter?.Invoke(complexProperty) == false)
                continue;

            members.TryAdd(complexProperty.Name, new EntityFieldInfo
            {
                Name = complexProperty.Name,
                ClrType = complexProperty.ClrType,
                ElementType = complexProperty.IsCollection ? complexProperty.ComplexType.ClrType : null,
                Kind = EntityFieldKind.ComplexProperty,
                Metadata = complexProperty,
                TargetType = complexProperty.ComplexType
            });
        }

        if (type is not IEntityType entityType)
            return members;

        foreach (var navigation in entityType.GetNavigations())
        {
            if (config.NavigationFilter?.Invoke(navigation) == false)
                continue;

            members.TryAdd(navigation.Name, CreateNavigation(navigation, EntityFieldKind.Navigation));
        }

        foreach (var navigation in entityType.GetSkipNavigations())
        {
            if (config.SkipNavigationFilter?.Invoke(navigation) == false)
                continue;

            members.TryAdd(navigation.Name, CreateNavigation(navigation, EntityFieldKind.SkipNavigation));
        }

        return members;
    }

    private static EntityFieldInfo CreateNavigation(INavigationBase navigation, EntityFieldKind kind)
    {
        return new EntityFieldInfo
        {
            Name = navigation.Name,
            ClrType = navigation.ClrType,
            ElementType = navigation.IsCollection ? navigation.TargetEntityType.ClrType : null,
            Kind = kind,
            Metadata = navigation,
            TargetType = navigation.TargetEntityType
        };
    }
}
