using Newtonsoft.Json;

namespace OrderIntake.Core;

/// <summary>
/// Payload accepted by POST /api/orders.
/// The public contract uses snake_case for customer_id, so the property is mapped explicitly.
/// </summary>
public sealed class OrderRequest
{
    [JsonProperty("customer_id")]
    public string? CustomerId { get; set; }

    public List<OrderLine> Items { get; set; } = new();
}

public sealed class OrderLine
{
    public string? Sku { get; set; }

    public int Quantity { get; set; }
}
