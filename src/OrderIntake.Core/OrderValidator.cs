namespace OrderIntake.Core;

public static class OrderValidator
{
    public const int MaxLines = 100;

    public static IReadOnlyList<string> Validate(OrderRequest? request)
    {
        var errors = new List<string>();

        if (request is null)
        {
            errors.Add("Request body is missing or is not valid JSON.");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(request.CustomerId))
        {
            errors.Add("customer_id is required.");
        }

        // "items": null and "items": [null] deserialize to nulls despite the initializer.
        var items = request.Items ?? [];
        if (items.Count == 0)
        {
            errors.Add("At least one item is required.");
        }
        else if (items.Count > MaxLines)
        {
            errors.Add($"An order can have at most {MaxLines} items.");
        }

        for (var i = 0; i < items.Count; i++)
        {
            var line = items[i];
            if (line is null)
            {
                errors.Add($"items[{i}] is required.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(line.Sku))
            {
                errors.Add($"items[{i}].sku is required.");
            }

            if (line.Quantity <= 0)
            {
                errors.Add($"items[{i}].quantity must be greater than zero.");
            }
        }

        return errors;
    }
}
