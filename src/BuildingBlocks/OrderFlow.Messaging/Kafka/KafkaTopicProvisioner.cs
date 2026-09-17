namespace OrderFlow.Messaging.Kafka;

using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderFlow.Contracts;

public sealed partial class KafkaTopicProvisioner(
    IOptions<KafkaOptions> options,
    ILogger<KafkaTopicProvisioner> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var o = options.Value;
        if (o.ManagedTopics.Length == 0) { return; }

        using var admin = new AdminClientBuilder(
            new AdminClientConfig { BootstrapServers = o.BootstrapServers }).Build();

        var specs = o.ManagedTopics
            .SelectMany(t => new[] { t, Topics.DeadLetter(t) })   // every topic gets a DLQ sibling
            .Distinct()
            .Select(t => new TopicSpecification
            {
                Name = t,
                NumPartitions = o.DefaultPartitions,
                ReplicationFactor = o.ReplicationFactor
            })
            .ToList();

        try
        {
            await admin.CreateTopicsAsync(specs);
            var topicNames = specs.ConvertAll(s => s.Name);
            LogProvisioned(logger, topicNames);
        }
        catch (CreateTopicsException ex) when (
            ex.Results.All(r => r.Error.Code is ErrorCode.NoError or ErrorCode.TopicAlreadyExists))
        {
            // Idempotent startup: every restart re-runs this, and already existing is success.
            //
            // NoError has to be tolerated alongside it, because a MIXED result is the normal
            // case the moment a topic is added to an existing deployment — some created, some
            // already there. Requiring every result to be TopicAlreadyExists would rethrow,
            // and a throw here stops the whole service from starting.
            LogTopicsAlreadyExist(logger);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    
    [LoggerMessage(Level = LogLevel.Information, Message = "Provisioned topics: {Topics}")]
    private static partial void LogProvisioned(ILogger logger, IEnumerable<string> topics);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Topics already exist.")]
    private static partial void LogTopicsAlreadyExist(ILogger logger);
}
