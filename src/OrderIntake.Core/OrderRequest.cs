using System.Text.Json.Serialization;

namespace OrderIntake.Core;

/// <summary>
/// Payload accepted by POST /api/orders.
/// The public contract uses snake_case for customer_id, so the property is mapped explicitly.
/// </summary>
/// <remarks>
/// Migration note: this used to be Newtonsoft's [JsonProperty("customer_id")].
/// System.Text.Json ignores that attribute without an error, so CustomerId would silently bind
/// to null. See RequestContractTests.
/// </remarks>
public sealed class OrderRequest
{
    [JsonPropertyName("customer_id")]
    public string? CustomerId { get; set; }

    public List<OrderLine> Items { get; set; } = new();
}

public sealed class OrderLine
{
    public string? Sku { get; set; }

    public int Quantity { get; set; }
}
