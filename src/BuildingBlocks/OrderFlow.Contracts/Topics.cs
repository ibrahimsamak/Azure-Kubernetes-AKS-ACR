namespace OrderFlow.Contracts;

public static class Topics
{
    // One topic per PRODUCER. Rationale: ordering is only guaranteed within a partition
    // of a topic, and we want Inventory's StockReserved/StockReleased for one order to
    // be strictly ordered relative to each other.
    public const string Orders = "orderflow.orders.v1";
    public const string Inventory = "orderflow.inventory.v1";
    public const string Payments = "orderflow.payments.v1";

    /// <summary>Poison messages go to a parallel topic, never back onto the main one.</summary>
    public static string DeadLetter(string topic) => $"{topic}.dlq";

    /// <summary>Maps an event CLR type to the topic that carries it.
    /// Called by the publisher so callers never pass a topic string by hand.</summary>
    public static string For(Type eventType) => eventType.Namespace switch
    {
        "OrderFlow.Contracts.Orders" => Orders,
        "OrderFlow.Contracts.Inventory" => Inventory,
        "OrderFlow.Contracts.Payments" => Payments,
        _ => throw new InvalidOperationException($"No topic mapped for {eventType.FullName}.")
    };
}
