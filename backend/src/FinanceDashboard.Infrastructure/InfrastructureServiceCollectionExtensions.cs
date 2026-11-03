using FinanceDashboard.Infrastructure.Importing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinanceDashboard.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection s, IConfiguration c)
    {
        s.AddDbContext<AppDbContext>(o => o.UseNpgsql(c.GetConnectionString("Default")));
        s.TryAddSingleton(TimeProvider.System);
        s.AddScoped<ImportService>();
        return s;
    }
}
