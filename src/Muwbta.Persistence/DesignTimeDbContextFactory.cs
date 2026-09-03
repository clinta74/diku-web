using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace Muwbta.Persistence;

/// <summary>
/// Used only by "dotnet ef" at design time so migrations can be added without booting the
/// server. The connection string here is never used to run the app - it only has to be
/// parseable for the model to be built.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<MuwbtaDbContext>
{
    private const string FallbackConnectionString =
        "Host=localhost;Port=5432;Database=dikuweb;Username=dikuweb;Password=dikuweb_dev_only";

    public MuwbtaDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Muwbta")
            ?? FallbackConnectionString;

        // Create and configure the Npgsql data source
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        // Enable dynamic JSON serialization for Dictionary<string, object> types (used in mob templates, etc)
        dataSourceBuilder.EnableDynamicJson();
        var dataSource = dataSourceBuilder.Build();

        var options = new DbContextOptionsBuilder<MuwbtaDbContext>()
            .UseNpgsql(dataSource)
            .Options;

        return new MuwbtaDbContext(options);
    }
}
