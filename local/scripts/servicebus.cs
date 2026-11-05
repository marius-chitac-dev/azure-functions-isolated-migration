#:package Azure.Messaging.ServiceBus@7.20.1
using Azure.Messaging.ServiceBus;

// Talks to the local Service Bus emulator, bypassing SubmitOrder's validation.
//   dotnet run local/scripts/servicebus.cs -- send '<body>'           put a raw message on "orders"
//   dotnet run local/scripts/servicebus.cs -- dead-letters [count]    wait for and list dead-lettered messages
const string Connection = "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";
await using var client = new ServiceBusClient(Connection);

switch (args.FirstOrDefault())
{
    case "send" when args.Length == 2:
        var message = new ServiceBusMessage(args[1]) { MessageId = Guid.NewGuid().ToString("N"), ContentType = "application/json" };
        await client.CreateSender("orders").SendMessageAsync(message);
        Console.WriteLine($"Sent {message.MessageId}: {args[1]}");
        return 0;

    case "dead-letters":
        var expected = args.Length > 1 ? int.Parse(args[1]) : 1;
        await using (var receiver = client.CreateReceiver("orders", new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter }))
        {
            for (var attempt = 1; attempt <= 30; attempt++)
            {
                // Peek, don't receive: the messages stay in the dead-letter queue.
                var peeked = await receiver.PeekMessagesAsync(maxMessages: 50, fromSequenceNumber: 0);
                if (peeked.Count >= expected)
                {
                    foreach (var m in peeked)
                    {
                        Console.WriteLine($"{m.MessageId}: {m.DeadLetterReason} ({m.DeadLetterErrorDescription}) body={m.Body}");
                    }
                    return 0;
                }
                await Task.Delay(1000);
            }
        }

        Console.WriteLine($"Fewer than {expected} dead-lettered message(s) after 30 s");
        return 1;

    default:
        Console.WriteLine("Usage: dotnet run local/scripts/servicebus.cs -- send '<body>' | dead-letters [count]");
        return 2;
}
