using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.ServiceBus;
using Microsoft.Extensions.Logging;
using OrderIntake.Core;
using OrderIntake.Services;

namespace OrderIntake.Functions;

public sealed class ProcessOrder
{
    // Case-insensitive on purpose: the producer and the consumer may run different serializers
    // during a deployment, and messages already in the queue must still be readable.
    private static readonly JsonSerializerOptions MessageJson = new(JsonSerializerDefaults.Web);

    private readonly IOrderStore _store;

    public ProcessOrder(IOrderStore store) => _store = store;

    [FunctionName("ProcessOrder")]
    public async Task Run(
        [ServiceBusTrigger("orders", Connection = "ServiceBusConnection", AutoCompleteMessages = false)]
        ServiceBusReceivedMessage message,
        ServiceBusMessageActions messageActions,
        ILogger log,
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
            log.LogWarning("Dead-lettered message {MessageId}: invalid JSON", message.MessageId);
            return;
        }

        if (order is null || string.IsNullOrWhiteSpace(order.OrderId) || string.IsNullOrWhiteSpace(order.CustomerId))
        {
            await messageActions.DeadLetterMessageAsync(
                message,
                deadLetterReason: "MissingFields",
                deadLetterErrorDescription: "orderId and customerId are required.");
            log.LogWarning("Dead-lettered message {MessageId}: missing fields", message.MessageId);
            return;
        }

        // Transient failures throw here. The message is not completed, its lock expires,
        // and Service Bus redelivers it until MaxDeliveryCount moves it to the dead-letter queue.
        await _store.UpsertAsync(order, cancellationToken);
        await messageActions.CompleteMessageAsync(message);

        log.LogInformation(
            "Stored order {OrderId} (delivery {DeliveryCount})",
            order.OrderId,
            message.DeliveryCount);
    }
}
