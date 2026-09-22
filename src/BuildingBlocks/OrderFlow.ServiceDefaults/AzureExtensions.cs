using Azure.Core;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.Hosting;

public static class AzureExtensions
{
    public static TBuilder AddOrderFlowAzure<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        TokenCredential credential = new DefaultAzureCredential();
        builder.Services.AddSingleton(credential);

        var vaultUri = builder.Configuration["KeyVault:Uri"];
        if (!string.IsNullOrWhiteSpace(vaultUri))
        {
            builder.Configuration.AddAzureKeyVault(
                new Uri(vaultUri),
                credential,
                new AzureKeyVaultConfigurationOptions
                {
                    ReloadInterval = TimeSpan.FromMinutes(5)
                });
        }

        return builder;
    }
}
