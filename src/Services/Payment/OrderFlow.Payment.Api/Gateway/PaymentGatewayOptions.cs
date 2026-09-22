namespace OrderFlow.Payment.Api.Gateway;

using System.ComponentModel.DataAnnotations;

public sealed class PaymentGatewayOptions
{
    public const string SectionName = "PaymentGateway";

    [Required]
    public string ApiKey { get; set; } = string.Empty;
}
