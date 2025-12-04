using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Lucene.EntityFramework;

/// <summary>
/// Entity Framework Core options extension that registers the Lucene query parser in the DbContext's service provider.
/// </summary>
public class LuceneQueryOptionsExtension : IDbContextOptionsExtension
{
    private readonly EntityFrameworkQueryParser _parser;
    private DbContextOptionsExtensionInfo? _info;

    /// <summary>
    /// Creates a new instance of the extension with the specified parser.
    /// </summary>
    /// <param name="parser">The query parser instance to register.</param>
    public LuceneQueryOptionsExtension(EntityFrameworkQueryParser parser)
    {
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
    }

    /// <summary>
    /// Gets the query parser instance.
    /// </summary>
    public EntityFrameworkQueryParser Parser => _parser;

    /// <inheritdoc />
    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    /// <inheritdoc />
    public void ApplyServices(IServiceCollection services)
    {
        services.AddSingleton(_parser);
    }

    /// <inheritdoc />
    public void Validate(IDbContextOptions options)
    {
        // No validation required
    }

    private sealed class ExtensionInfo : DbContextOptionsExtensionInfo
    {
        public ExtensionInfo(IDbContextOptionsExtension extension) : base(extension)
        {
        }

        private new LuceneQueryOptionsExtension Extension
            => (LuceneQueryOptionsExtension)base.Extension;

        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "using LuceneQuery ";

        public override int GetServiceProviderHashCode() => Extension._parser.GetHashCode();

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            => other is ExtensionInfo otherInfo && ReferenceEquals(Extension._parser, otherInfo.Extension._parser);

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
            debugInfo["LuceneQuery"] = "enabled";
        }
    }
}
