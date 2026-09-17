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
                var handler = TestHost.Resolve<OrderPlacedHandler>();
                await handler.HandleAsync(message, CancellationToken.None);
            });
    }
}
