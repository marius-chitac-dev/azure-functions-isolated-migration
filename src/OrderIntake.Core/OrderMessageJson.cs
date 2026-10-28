using System.Text.Json;

namespace OrderIntake.Core;

/// <summary>
/// Serializer options ProcessOrder reads queue messages with.
/// </summary>
/// <remarks>
/// Case-insensitive on purpose: during cut-over the queue still holds messages written by the
/// in-process producer (Newtonsoft, PascalCase). The isolated consumer must read both shapes.
/// See MessageContractTests.
/// </remarks>
public static class OrderMessageJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
