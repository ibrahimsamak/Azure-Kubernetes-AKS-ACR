namespace OrderFlow.Order.Api.Security;

/// <summary>Names used in Entra (scope and app-role VALUES) and in [Authorize]. Constants, because a
/// typo here doesn't fail a build — it fails every request with 403.</summary>
public static class OrderPolicies
{
    public const string Scope = "Orders.ReadWrite";
    public const string CustomerRole = "OrderFlow.Customer";
    public const string SupportRole = "OrderFlow.Support";

    public const string PlaceOrder = "orders.place";
    public const string ReadOrders = "orders.read";
}
