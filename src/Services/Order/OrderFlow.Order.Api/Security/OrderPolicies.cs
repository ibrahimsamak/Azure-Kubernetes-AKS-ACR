namespace OrderFlow.Order.Api.Security;

public static class OrderPolicies
{
    public const string Scope = "Orders.ReadWrite";
    public const string CustomerRole = "OrderFlow.Customer";
    public const string SupportRole = "OrderFlow.Support";

    public const string PlaceOrder = "orders.place";
    public const string ReadOrders = "orders.read";
}
