using System.Text.Json;
using OrderIntake.Core;
using Xunit;

namespace OrderIntake.Tests;

/// <summary>
/// Pins the queue message contract to the options ProcessOrder reads with.
/// During cut-over the queue still holds messages from the in-process producer.
/// </summary>
public class MessageContractTests
{
    [Fact]
    public void Message_from_in_process_producer_is_read()
    {
        var sent = new OrderMessage(
            "0f8c2d4e6a1b4c3d9e7f5a2b1c0d9e8f",
            "c-42",
            new List<OrderLine> { new() { Sku = "SKU-001", Quantity = 2 } },
            new DateTimeOffset(2026, 10, 28, 9, 30, 15, TimeSpan.FromHours(2)));

        // The in-process output binding serialized with Newtonsoft's defaults, so names are PascalCase.
        var body = Newtonsoft.Json.JsonConvert.SerializeObject(sent);
        Assert.Contains("\"CustomerId\":", body);

        var received = JsonSerializer.Deserialize<OrderMessage>(body, OrderMessageJson.Options);

        Assert.NotNull(received);
        Assert.Equal(sent.OrderId, received!.OrderId);
        Assert.Equal(sent.CustomerId, received.CustomerId);
        Assert.Equal(sent.AcceptedAt, received.AcceptedAt);
        var line = Assert.Single(received.Items);
        Assert.Equal("SKU-001", line.Sku);
        Assert.Equal(2, line.Quantity);
    }
}
