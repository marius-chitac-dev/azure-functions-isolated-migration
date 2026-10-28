using Newtonsoft.Json;
using OrderIntake.Core;
using Xunit;

namespace OrderIntake.Tests;

/// <summary>
/// Pins the public request contract to the deserializer the HTTP function actually uses.
/// In the in-process model that is Newtonsoft.Json.
/// </summary>
public class RequestContractTests
{
    private static string Fixture() => File.ReadAllText(Path.Combine("fixtures", "order-request.json"));

    [Fact]
    public void Snake_case_customer_id_binds()
    {
        var request = JsonConvert.DeserializeObject<OrderRequest>(Fixture());

        Assert.NotNull(request);
        Assert.Equal("c-42", request!.CustomerId);
        Assert.Equal(2, request.Items.Count);
        Assert.Empty(OrderValidator.Validate(request));
    }
}
