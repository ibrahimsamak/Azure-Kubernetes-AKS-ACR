namespace OrderFlow.Contracts;

public static class Topics
{
    public const string Orders = "orderflow.orders.v1";
    public const string Inventory = "orderflow.inventory.v1";
    public const string Payments = "orderflow.payments.v1";

    public static string DeadLetter(string topic) => $"{topic}.dlq";

    public static string For(Type eventType) => eventType.Namespace switch
    {
        "OrderFlow.Contracts.Orders" => Orders,
        "OrderFlow.Contracts.Inventory" => Inventory,
        "OrderFlow.Contracts.Payments" => Payments,
        _ => throw new InvalidOperationException($"No topic mapped for {eventType.FullName}.")
    };
}
