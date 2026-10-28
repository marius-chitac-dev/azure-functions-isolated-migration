using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OrderIntake.Core;

namespace OrderIntake.Functions;

public sealed class SubmitOrder
{
    [FunctionName("SubmitOrder")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "orders")] HttpRequest req,
        [ServiceBus("orders", Connection = "ServiceBusConnection")] IAsyncCollector<OrderMessage> orders,
        ILogger log)
    {
        OrderRequest? request;
        try
        {
            // Newtonsoft.Json honours [JsonProperty("customer_id")] on OrderRequest.
            using var reader = new StreamReader(req.Body);
            request = JsonConvert.DeserializeObject<OrderRequest>(await reader.ReadToEndAsync());
        }
        catch (JsonException)
        {
            request = null;
        }

        var errors = OrderValidator.Validate(request);
        if (errors.Count > 0)
        {
            return new BadRequestObjectResult(new { errors });
        }

        var order = OrderMessage.From(request!, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow);
        await orders.AddAsync(order);

        log.LogInformation("Accepted order {OrderId} for customer {CustomerId}", order.OrderId, order.CustomerId);
        return new AcceptedResult($"/api/orders/{order.OrderId}", new { orderId = order.OrderId });
    }
}
