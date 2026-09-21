namespace OrderFlow.Payment.Api.Gateway;

using System.ComponentModel.DataAnnotations;

public sealed class PaymentGatewayOptions
{
    public const string SectionName = "PaymentGateway";

    /// <summary>From Key Vault secret "PaymentGateway--ApiKey" in Azure; user-secrets locally. Never logged.</summary>
    [Required]
    public string ApiKey { get; set; } = string.Empty;
}
