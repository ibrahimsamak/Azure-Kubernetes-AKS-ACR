namespace OrderFlow.Messaging.Kafka;
using Azure.Core;
using Azure.Identity;
using Confluent.Kafka;

internal static class AzureKafkaAuth
{
    public static void Apply(ClientConfig config, KafkaOptions options)
    {
        config.BootstrapServers = options.BootstrapServers;
        if (options.AuthMode != KafkaAuthMode.AzureAd)
        {
            return;
        }
        config.SecurityProtocol = SecurityProtocol.SaslSsl;
        config.SaslMechanism = SaslMechanism.OAuthBearer;

        config.SocketKeepaliveEnable = true;
        config.MetadataMaxAgeMs = 180_000;
        config.Set("connections.max.idle.ms", "180000");
    }

    public static void RefreshToken(IClient client, TokenCredential credential, KafkaOptions options)
    {
        var host = options.BootstrapServers.Split(':')[0];
        var scope = $"https://{host}/.default";

        try
        {
            AccessToken token = credential.GetToken(new TokenRequestContext([scope]), CancellationToken.None);

            client.OAuthBearerSetToken(
                tokenValue: token.Token,
                lifetimeMs: token.ExpiresOn.ToUnixTimeMilliseconds(),
                principalName: "orderflow");
        }
        catch (AuthenticationFailedException ex)
        {
            client.OAuthBearerSetTokenFailure(ex.Message);
        }
    }
}
