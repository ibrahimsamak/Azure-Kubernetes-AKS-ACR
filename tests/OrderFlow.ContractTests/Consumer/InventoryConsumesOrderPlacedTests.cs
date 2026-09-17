namespace OrderFlow.ContractTests.Consumer;

using OrderFlow.Contracts.Orders;
using OrderFlow.Inventory.Api.Handlers;
using PactNet;
using PactNet.Matchers;
using PactNet.Output.Xunit;
using Xunit;
using Xunit.Abstractions;

public sealed class InventoryConsumesOrderPlacedTests
{
    private readonly IMessagePactBuilderV3 _pact;

    public InventoryConsumesOrderPlacedTests(ITestOutputHelper output)
    {
        // V3, not V4: PactNet's V4 message verification routes the provider side through a
        // "message://" transport its own Rust core cannot build a request for. V3 message
        // pacts verify over HTTP and work.
        _pact = Pact.V3("inventory-service", "order-service",
            new PactConfig { PactDir = "../../../../pacts", Outputters = [new XunitOutput(output)] })
            .WithMessageInteractions();
    }

    [Fact]
    public async Task Inventory_can_handle_an_OrderPlaced_message()
    {
        await _pact
            .ExpectsToReceive("an OrderPlaced event with two lines")
            .WithJsonContent(new
            {
                // MATCHERS, not literals: we assert on TYPE and SHAPE, not on values.
                // Asserting exact values would make the provider test fail for no reason
                // every time a sample GUID changed.
                messageId = Match.Type("0192f3a0-0000-7000-8000-000000000000"),
                correlationId = Match.Type("0192f3a0-0000-7000-8000-000000000000"),
                occurredOnUtc = Match.Type("2026-09-11T10:00:00Z"),
                orderId = Match.Type("0192f3a0-0000-7000-8000-000000000000"),
                customerId = Match.Type("CUST-1"),
                totalAmount = Match.Decimal(59.98),
                currency = Match.Regex("CAD", "^[A-Z]{3}$"),
                lines = Match.MinType(new
                {
                    sku = Match.Type("SKU-1"),
                    quantity = Match.Integer(2),
                    unitPrice = Match.Decimal(29.99)
                }, 1)
            })
            .VerifyAsync<OrderPlaced>(async message =>
            {
                // The REAL handler runs against the message Pact generated. If our
                // deserialization or handler logic cannot cope with this shape, it fails here.
                var handler = TestHost.Resolve<OrderPlacedHandler>();
                await handler.HandleAsync(message, CancellationToken.None);
            });
    }
}
