using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using OrderIntake.Core;

namespace OrderIntake.Functions;

public sealed class SubmitOrder(ILogger<SubmitOrder> logger)
{
    [Function("SubmitOrder")]
    public async Task<SubmitOrderOutput> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "orders")] HttpRequest req)
    {
        OrderRequest? request;
        try
        {
            // System.Text.Json now. Body I/O must be async: synchronous reads throw under
            // ASP.NET Core integration.
            request = await req.ReadFromJsonAsync<OrderRequest>(req.HttpContext.RequestAborted);
        }
        catch (InvalidOperationException)
        {
            // No JSON Content-Type, or a charset that isn't a known encoding. The in-process version
            // read any body; ReadFromJsonAsync refuses these, which would otherwise surface as a 500.
            return new SubmitOrderOutput { Result = new UnsupportedMediaTypeResult() };
        }
        catch (JsonException)
        {
            request = null;
        }

        var errors = OrderValidator.Validate(request);
        if (errors.Count > 0)
        {
            // Order stays null, so nothing is written to the queue.
            return new SubmitOrderOutput { Result = new BadRequestObjectResult(new { errors }) };
        }

        var order = OrderMessage.From(request!, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow);

        logger.LogInformation("Accepted order {OrderId} for customer {CustomerId}", order.OrderId, order.CustomerId);
        return new SubmitOrderOutput
        {
            Result = new AcceptedResult($"/api/orders/{order.OrderId}", new { orderId = order.OrderId }),
            Order = order,
        };
    }
}

/// <summary>
/// IAsyncCollector parameters are gone. With more than one output, the function returns a type
/// whose properties carry the output bindings.
/// </summary>
public sealed class SubmitOrderOutput
{
    [HttpResult]
    public required IActionResult Result { get; init; }

    [ServiceBusOutput("orders", Connection = "ServiceBusConnection")]
    public OrderMessage? Order { get; init; }
}
