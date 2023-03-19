using DependencyStore.Repositories;
using DependencyStore.Repositories.Contracts;
using DependencyStore.Services;
using DependencyStore.Services.Contracts;
using Microsoft.Data.SqlClient;

namespace DependencyStore.Extensions;
public static class DependenciesExtension
{
    public static void AddConfiguration(this IServiceCollection services)
    {
        services.AddSingleton<Configuration>();
    }

    public static void AddSqlConnection(
        this IServiceCollection services,
        WebApplicationBuilder builder)
    {
        services.AddScoped<SqlConnection>(
            x => new SqlConnection(
                builder
                            .Configuration
                            .GetConnectionString("DefaultConnection"))
        );
    }

    public static void AddRepositories(this IServiceCollection services)
    {
        services.AddTransient<ICustomerRepository, CustomerRepository>();
        services.AddTransient<IPromoCodeRepository, PromoCodeRepository>();
    }

    public static void AddServices(this IServiceCollection services)
    {
        services.AddTransient<IDeliveryFeeService, DeliveryFeeService>();
    }
}