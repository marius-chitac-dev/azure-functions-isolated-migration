namespace OrderIntake.Core;

/// <summary>
/// Message placed on the "orders" queue and stored in Cosmos DB.
/// OrderId doubles as the Cosmos document id, which makes processing idempotent.
/// </summary>
public sealed record OrderMessage(
    string OrderId,
    string CustomerId,
    IReadOnlyList<OrderLine> Items,
    DateTimeOffset AcceptedAt)
{
    public static OrderMessage From(OrderRequest request, string orderId, DateTimeOffset acceptedAt) =>
        new(orderId, request.CustomerId!, request.Items, acceptedAt);
}
