using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using OrderIntake.Core;
using OrderIntake.Services;

namespace OrderIntake.Functions;

public sealed class ProcessOrder(IOrderStore store, ILogger<ProcessOrder> logger)
{
    // Case-insensitive on purpose: during cut-over the queue still holds messages written by the
    // in-process producer (Newtonsoft, PascalCase). The isolated consumer must read both shapes.
    private static readonly JsonSerializerOptions MessageJson = new(JsonSerializerDefaults.Web);

    [Function("ProcessOrder")]
    public async Task Run(
        [ServiceBusTrigger("orders", Connection = "ServiceBusConnection", AutoCompleteMessages = false)]
        ServiceBusReceivedMessage message,
        ServiceBusMessageActions messageActions,
        CancellationToken cancellationToken)
    {
        OrderMessage? order;
        try
        {
            order = message.Body.ToObjectFromJson<OrderMessage>(MessageJson);
        }
        catch (JsonException ex)
        {
            // Poison message: retrying will never fix it, so dead-letter immediately.
            await messageActions.DeadLetterMessageAsync(
                message,
                deadLetterReason: "InvalidJson",
                deadLetterErrorDescription: ex.Message);
            logger.LogWarning("Dead-lettered message {MessageId}: invalid JSON", message.MessageId);
            return;
        }

        if (order is null || string.IsNullOrWhiteSpace(order.OrderId) || string.IsNullOrWhiteSpace(order.CustomerId))
        {
            await messageActions.DeadLetterMessageAsync(
                message,
                deadLetterReason: "MissingFields",
                deadLetterErrorDescription: "orderId and customerId are required.");
            logger.LogWarning("Dead-lettered message {MessageId}: missing fields", message.MessageId);
            return;
        }

        // Transient failures throw here. The message is not completed, its lock expires,
        // and Service Bus redelivers it until MaxDeliveryCount moves it to the dead-letter queue.
        await store.UpsertAsync(order, cancellationToken);
        await messageActions.CompleteMessageAsync(message);

        logger.LogInformation(
            "Stored order {OrderId} (delivery {DeliveryCount})",
            order.OrderId,
            message.DeliveryCount);
    }
}
