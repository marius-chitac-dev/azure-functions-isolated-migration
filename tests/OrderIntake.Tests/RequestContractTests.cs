using System.Text.Json;
using OrderIntake.Core;
using Xunit;

namespace OrderIntake.Tests;

/// <summary>
/// Pins the public request contract to the deserializer the HTTP function actually uses.
/// After the migration that is System.Text.Json with web defaults (ReadFromJsonAsync).
/// </summary>
public class RequestContractTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static string Fixture() => File.ReadAllText(Path.Combine("fixtures", "order-request.json"));

    [Fact]
    public void Snake_case_customer_id_binds()
    {
        var request = JsonSerializer.Deserialize<OrderRequest>(Fixture(), WebJson);

        Assert.NotNull(request);
        Assert.Equal("c-42", request!.CustomerId);
        Assert.Equal(2, request.Items.Count);
        Assert.Empty(OrderValidator.Validate(request));
    }

    /// <summary>
    /// Documents the trap this test suite exists for: the pre-migration model, read by
    /// System.Text.Json, deserializes without any error and simply leaves CustomerId null.
    /// </summary>
    [Fact]
    public void Newtonsoft_attribute_is_silently_ignored_by_System_Text_Json()
    {
        var request = JsonSerializer.Deserialize<LegacyOrderRequest>(Fixture(), WebJson);

        Assert.NotNull(request);
        Assert.Null(request!.CustomerId);
    }

    private sealed class LegacyOrderRequest
    {
        [Newtonsoft.Json.JsonProperty("customer_id")]
        public string? CustomerId { get; set; }

        public List<OrderLine> Items { get; set; } = new();
    }
}
