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

        // Event Hubs closes connections idle for ~240s. If the client doesn't know, the first send
        // after a quiet period hits a dead socket, times out, and retries. Recycle earlier ourselves.
    
        config.SocketKeepaliveEnable = true;
        config.MetadataMaxAgeMs = 180_000;
        config.Set("connections.max.idle.ms", "180000");
    }

    public static void RefreshToken(IClient client, TokenCredential credential, KafkaOptions options)
    {
        // Token audience = the namespace host: https://evhns-orderflow-ibs01.servicebus.windows.net/.default
        var host = options.BootstrapServers.Split(':')[0];
        var scope = $"https://{host}/.default";

        try
        {
            AccessToken token = credential.GetToken(new TokenRequestContext([scope]), CancellationToken.None);

            client.OAuthBearerSetToken(
                tokenValue: token.Token,
                lifetimeMs: token.ExpiresOn.ToUnixTimeMilliseconds(),
                principalName: "orderflow");   // informational only for Event Hubs
        }
        catch (AuthenticationFailedException ex)
        {
            // Tell librdkafka the refresh failed so it retries and surfaces an error,
            // instead of waiting forever for a token that will never arrive.
            client.OAuthBearerSetTokenFailure(ex.Message);
        }
    }
}