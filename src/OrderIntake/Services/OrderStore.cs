using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using OrderIntake.Core;

namespace OrderIntake.Services;

public interface IOrderStore
{
    Task UpsertAsync(OrderMessage order, CancellationToken cancellationToken);
}

/// <summary>
/// Stores orders in Cosmos DB. The document id is the order id and the partition key is the
/// customer id, so a redelivered message overwrites the same document instead of duplicating it.
/// </summary>
public sealed class CosmosOrderStore : IOrderStore
{
    private readonly Container _container;

    public CosmosOrderStore(CosmosClient client, IConfiguration configuration)
    {
        _container = client.GetContainer(
            configuration["CosmosDatabase"] ?? "shop",
            configuration["CosmosContainer"] ?? "orders");
    }

    public Task UpsertAsync(OrderMessage order, CancellationToken cancellationToken) =>
        _container.UpsertItemAsync(
            new OrderDocument(order.OrderId, order.CustomerId, order.Items, order.AcceptedAt),
            new PartitionKey(order.CustomerId),
            cancellationToken: cancellationToken);

    private sealed record OrderDocument(
        string Id,
        string CustomerId,
        IReadOnlyList<OrderLine> Items,
        DateTimeOffset AcceptedAt);
}
