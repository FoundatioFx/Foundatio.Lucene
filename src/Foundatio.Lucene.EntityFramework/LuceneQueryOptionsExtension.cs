using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Lucene.EntityFramework;

/// <summary>
/// An EF Core options extension that associates an <see cref="EntityFrameworkQueryParser"/> with a
/// <c>DbContext</c>. The parser is read from the context options rather than registered in EF Core's internal service
/// provider, so every context shares one internal service provider regardless of which parser it uses.
/// </summary>
public sealed class LuceneQueryOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    /// <summary>
    /// Creates the extension for the specified parser.
    /// </summary>
    public LuceneQueryOptionsExtension(EntityFrameworkQueryParser parser)
    {
        Parser = parser ?? throw new ArgumentNullException(nameof(parser));
    }

    /// <summary>
    /// The query parser associated with the context.
    /// </summary>
    public EntityFrameworkQueryParser Parser { get; }

    /// <inheritdoc />
    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    /// <inheritdoc />
    public void ApplyServices(IServiceCollection services)
    {
    }

    /// <inheritdoc />
    public void Validate(IDbContextOptions options)
    {
    }

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "using LuceneQuery ";

        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
            debugInfo["LuceneQuery"] = "1";
        }
    }
}
