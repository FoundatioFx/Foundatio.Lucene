using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Foundatio.Lucene.EntityFramework.Tests;

public class DbContextIntegrationTests
{
    private static SampleContext CreateContext(string database, Action<DbContextOptionsBuilder<SampleContext>> configure, bool seed = false)
    {
        var builder = new DbContextOptionsBuilder<SampleContext>()
            .UseInMemoryDatabase(database)
            .ConfigureWarnings(w => w.Throw(CoreEventId.ManyServiceProvidersCreatedWarning));
        configure(builder);

        var db = new SampleContext(builder.Options);
        if (seed)
            SampleData.Seed(db);

        return db;
    }

    [Fact]
    public void AddLuceneQuery_WithSharedParser_ExposesParserToContextAndDbSet()
    {
        var parser = new EntityFrameworkQueryParser(c => c.SetDefaultFields("Name"));
        using var db = CreateContext(Guid.NewGuid().ToString(), b => b.AddLuceneQuery(parser), seed: true);

        Assert.Same(parser, db.GetQueryParser());
        Assert.Equal(["John Doe"], db.Employees.Where("age:30").Names());
        Assert.Equal(["Jane Smith"], db.Employees.Where("Ja*").Names());
        Assert.Equal(["Bob Wilson"], db.Employees.Where("Sen*", new EntityFrameworkQueryOptions { DefaultFields = ["Title"] }).Names());
        Assert.Equal(4, db.Employees.Where("").Count());
        Assert.Equal(4, db.Employees.Where((string?)null).Count());
    }

    [Fact]
    public void AddLuceneQuery_WithConfigureAction_CreatesConfiguredParser()
    {
        using var db = CreateContext(Guid.NewGuid().ToString(), b => b.AddLuceneQuery(c => c.UseEntityTypePropertyFilter(p => p.Name != "PasswordHash")), seed: true);

        Assert.NotNull(db.GetQueryParser());
        Assert.Throws<QueryValidationException>(() => db.Employees.Where("passwordhash:*").ToList());
    }

    [Fact]
    public void DbSetWhere_WithoutRegisteredParser_ThrowsInvalidOperationException()
    {
        using var db = CreateContext(Guid.NewGuid().ToString(), _ => { }, seed: true);

        Assert.Null(db.GetQueryParser());
        var ex = Assert.Throws<InvalidOperationException>(() => db.Employees.Where("age:30"));
        Assert.Contains("AddLuceneQuery", ex.Message);
    }

    [Fact]
    public void AddLuceneQuery_With50ContextsAndDistinctParsers_SharesOneInternalServiceProvider()
    {
        string database = Guid.NewGuid().ToString();
        var providers = new HashSet<object>(ReferenceEqualityComparer.Instance);

        for (int i = 0; i < 50; i++)
        {
            using var shared = CreateContext(database, b => b.AddLuceneQuery(new EntityFrameworkQueryParser(c => c.SetDefaultFields("Name"))));
            using var configured = CreateContext(database, b => b.AddLuceneQuery(c => c.SetMaxFieldDepth(i % 5)));
            providers.Add(shared.GetService<IModelSource>());
            providers.Add(configured.GetService<IModelSource>());

            Assert.Empty(shared.Employees.Where("name:x").ToList());
        }

        Assert.Single(providers);
    }

    [Fact]
    public void AddLuceneQuery_WithSameNonCapturingConfigureDelegate_ReusesParser()
    {
        string database = Guid.NewGuid().ToString();
        static void Configure(EntityFrameworkQueryParserConfiguration c) => c.SetDefaultFields("Name");
        Action<EntityFrameworkQueryParserConfiguration> configure = Configure;

        using var first = CreateContext(database, b => b.AddLuceneQuery(configure));
        using var second = CreateContext(database, b => b.AddLuceneQuery(configure));

        Assert.Same(first.GetQueryParser(), second.GetQueryParser());
    }

    [Fact]
    public void Where_OnComposedQuery_FindsEntityTypeFromQueryRoot()
    {
        using var db = SampleData.CreateInMemory();
        var parser = new EntityFrameworkQueryParser();

        var query = db.Employees.AsNoTracking().Where(e => e.IsActive).OrderBy(e => e.Name).Where("age:>=35", parser);

        Assert.Equal(["Bob Wilson", "Jane Smith"], query.Names());
    }

    [Fact]
    public void Where_WithProjectionToAnotherEntity_UsesTheModelOfTheQuery()
    {
        using var db = SampleData.CreateInMemory();
        var parser = new EntityFrameworkQueryParser();

        var companies = db.Employees.Select(e => e.Company).Where("foundedyear:>2010", parser).Select(c => c.Name).Distinct();

        Assert.Equal(["Tech Solutions"], companies);
    }
}
