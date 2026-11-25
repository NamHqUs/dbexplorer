using DbExplorer.Application.Abstractions;
using DbExplorer.Infrastructure.Persistence;
using DbExplorer.Infrastructure.SqlServer;
using DbExplorer.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DbExplorer.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var appDbPath = configuration.GetConnectionString("AppDb")
            ?? throw new InvalidOperationException("ConnectionStrings:AppDb is not configured.");

        services.AddDbContext<AppDbContext>(o => o.UseSqlite(appDbPath));

        services.AddIdentityCore<IdentityUser>(o =>
            {
                o.Password.RequiredLength = 10;
                o.Password.RequireNonAlphanumeric = false;
                o.User.RequireUniqueEmail = false;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        services.AddDataProtection().SetApplicationName("DbExplorer");
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        services.AddScoped<IConnectionService, Persistence.ConnectionService>();
        services.AddSingleton<ISqlServerConnectionTester, SqlServerConnectionTester>();
        services.AddMemoryCache();
        services.Configure<Metadata.SchemaCacheOptions>(configuration.GetSection(Metadata.SchemaCacheOptions.SectionName));
        services.AddSingleton<ISchemaLoader, Metadata.SqlServerSchemaLoader>();
        services.AddScoped<ISchemaService, Metadata.SchemaService>();
        services.AddScoped<ITableDataService, Data.TableDataService>();
        services.AddScoped<IRelationService, Relations.RelationService>();
        services.AddScoped<IQueryService, Queries.QueryService>();
        services.AddScoped<ISavedQueryService, Queries.SavedQueryService>();

        return services;
    }
}
