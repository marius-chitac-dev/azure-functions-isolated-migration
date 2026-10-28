using OrderIntake.Core;
using Xunit;

namespace OrderIntake.Tests;

public class OrderValidatorTests
{
    [Fact]
    public void Valid_request_has_no_errors()
    {
        var request = new OrderRequest
        {
            CustomerId = "c-42",
            Items = { new OrderLine { Sku = "SKU-001", Quantity = 1 } },
        };

        Assert.Empty(OrderValidator.Validate(request));
    }

    [Fact]
    public void Null_request_is_rejected()
    {
        Assert.Single(OrderValidator.Validate(null));
    }

    [Fact]
    public void Missing_customer_and_bad_line_are_reported_together()
    {
        var request = new OrderRequest
        {
            Items = { new OrderLine { Sku = "", Quantity = 0 } },
        };

        var errors = OrderValidator.Validate(request);

        Assert.Contains("customer_id is required.", errors);
        Assert.Contains("items[0].sku is required.", errors);
        Assert.Contains("items[0].quantity must be greater than zero.", errors);
    }
}
