// using Azure.Core;
// using Azure.Extensions.AspNetCore.Configuration.Secrets;
// using Azure.Identity;
// using Microsoft.Extensions.Configuration;
// using Microsoft.Extensions.DependencyInjection;

// namespace Microsoft.Extensions.Hosting;

// public static class AzureExtensions
// {
//     /// <summary>
//     /// Registers the process-wide Azure credential and, when configured, Key Vault as a configuration source.
//     /// </summary>
//     public static TBuilder AddOrderFlowAzure<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
//     {
//         // In an AKS pod:   WorkloadIdentityCredential (reads AZURE_CLIENT_ID / AZURE_TENANT_ID /
//         //                  AZURE_FEDERATED_TOKEN_FILE injected by the Workload Identity webhook).
//         // On your laptop:  falls through to Visual Studio / Azure CLI credentials (az login).
//         // Nothing here is a secret. Creating the credential makes no network call.
//         TokenCredential credential = new DefaultAzureCredential();
//         builder.Services.AddSingleton(credential);

//         var vaultUri = builder.Configuration["KeyVault:Uri"];
//         if (!string.IsNullOrWhiteSpace(vaultUri))
//         {
//             // Secret "PaymentGateway--ApiKey" becomes configuration key "PaymentGateway:ApiKey".
//             // Added LAST, so Key Vault wins over appsettings and env vars.
//             // Startup FAILS if the vault is unreachable or access is denied — deliberately:
//             // a pod that can't read its secrets should crash visibly, not run half-configured.
//             builder.Configuration.AddAzureKeyVault(
//                 new Uri(vaultUri),
//                 credential,
//                 new AzureKeyVaultConfigurationOptions
//                 {
//                     // Pick up rotated secrets without a restart (IOptionsMonitor consumers see them).
//                     ReloadInterval = TimeSpan.FromMinutes(5)
//                 });
//         }

//         return builder;
//     }
// }


using Azure.Core;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.Hosting;

public static class AzureExtensions
{
    /// <summary>The process-wide Azure credential.
    /// In an AKS pod:  WorkloadIdentityCredential (AZURE_CLIENT_ID / AZURE_TENANT_ID /
    ///                 AZURE_FEDERATED_TOKEN_FILE injected by the Workload Identity webhook).
    /// On a laptop:    falls through to Visual Studio / Azure CLI credentials (az login).
    /// Constructing it makes no network call; it only acquires a token when something asks.</summary>
    public static TokenCredential Credential { get; } = new DefaultAzureCredential();

    /// <summary>Registers <see cref="Credential"/> and, when configured, Key Vault as a configuration source.</summary>
    public static TBuilder AddOrderFlowAzure<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton(Credential);

        var vaultUri = builder.Configuration["KeyVault:Uri"];
        if (!string.IsNullOrWhiteSpace(vaultUri))
        {
            // Secret "PaymentGateway--ApiKey" becomes configuration key "PaymentGateway:ApiKey".
            // Added LAST, so Key Vault wins over appsettings and env vars. Startup FAILS if the vault
            // is unreachable or access is denied — a pod that can't read its secrets should crash visibly.
            builder.Configuration.AddAzureKeyVault(
                new Uri(vaultUri),
                Credential,
                new AzureKeyVaultConfigurationOptions { ReloadInterval = TimeSpan.FromMinutes(5) });
        }

        return builder;
    }
}
