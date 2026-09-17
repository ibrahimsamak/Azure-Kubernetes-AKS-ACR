namespace OrderFlow.ContractTests.Provider;

using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using OrderFlow.Order.Application.EventMapping;
using OrderFlow.Order.Domain.Orders;
using OrderFlow.Order.Domain.Orders.Events;
using PactNet.Output.Xunit;
using PactNet.Verifier;
using Xunit;
using Xunit.Abstractions;

public sealed class OrderProducesOrderPlacedTests(ITestOutputHelper output)
{
    /// <summary>Production serializes with Web defaults, so the provider has to produce
    /// camelCase here or it would "honour" a contract it does not actually satisfy.</summary>
    private static readonly JsonSerializerSettings Camel = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver()
    };

    [Fact]
    public void Order_service_honours_every_consumer_pact()
    {
        using var verifier = new PactVerifier(
            new PactVerifierConfig { Outputters = [new XunitOutput(output)] });

        verifier
            .MessagingProvider("order-service", Camel)
            .WithProviderMessages(scenarios =>
            {
                scenarios.Add("an OrderPlaced event with two lines", scenario => scenario.WithContent(() =>
                {
                    // Build the event the same way production does: through the aggregate
                    // and the real mapper, not by hand. Otherwise the test proves nothing
                    // about the code that actually runs.
                    var order = Order.Create("CUST-1", "CAD",
                    [
                        ("SKU-1", 2, 29.99m),
                        ("SKU-2", 1, 12.50m)
                    ]);

                    var domainEvent = order.DomainEvents.OfType<OrderPlacedDomainEvent>().Single();
                    return DomainToIntegrationEvent.Map(domainEvent)!;
                }));
            })
            .WithFileSource(new FileInfo("../../../../pacts/inventory-service-order-service.json"))
            .Verify();
    }
}
