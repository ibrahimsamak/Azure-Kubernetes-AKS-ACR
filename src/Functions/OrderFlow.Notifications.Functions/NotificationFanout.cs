namespace OrderFlow.Notifications.Functions;

using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

/// <summary>Matches Notification.Api's ServiceBusNotificationPublisher payload (a contract, kept deliberately small).</summary>
public sealed record NotificationRequested(Guid OrderId, string CustomerId, string Kind, string[] Channels);

public sealed partial class NotificationFanout(ILogger<NotificationFanout> logger)
{
    private const string Topic = "notifications";
    private const string Connection = "ServiceBusConnection";   // -> ServiceBusConnection__fullyQualifiedNamespace

    [Function("SendEmail")]
    public void SendEmail(
        [ServiceBusTrigger(Topic, "email", Connection = Connection)] ServiceBusReceivedMessage message)
    {
        var n = Parse(message);
        // A real implementation calls SendGrid / Azure Communication Services here.
        LogEmail(logger, n.CustomerId, n.OrderId, n.Kind, message.DeliveryCount, message.MessageId);
    }

    [Function("SendSms")]
    public void SendSms(
        [ServiceBusTrigger(Topic, "sms", Connection = Connection)] ServiceBusReceivedMessage message)
    {
        var n = Parse(message);
        LogSms(logger, n.CustomerId, n.OrderId, n.Kind, message.DeliveryCount);
    }

    private static NotificationRequested Parse(ServiceBusReceivedMessage message)
    {
        var n = message.Body.ToObjectFromJson<NotificationRequested>()
            ?? throw new InvalidOperationException($"Message {message.MessageId} has an empty body.");

        // Demo hook: exception -> abandon -> redelivery ... -> dead-letter after MaxDeliveryCount (10).
        if (n.CustomerId == "POISON")
        {
            throw new InvalidOperationException($"Simulated channel failure for message {message.MessageId}.");
        }

        return n;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "EMAIL -> customer {CustomerId}: order {OrderId} {Kind} (attempt {Attempt}, messageId {MessageId})")]
    private static partial void LogEmail(ILogger logger, string customerId, Guid orderId, string kind, int attempt, string messageId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "SMS -> customer {CustomerId}: order {OrderId} {Kind} (attempt {Attempt})")]
    private static partial void LogSms(ILogger logger, string customerId, Guid orderId, string kind, int attempt);
}
