namespace Homestead.WebApi.Modules.Mcp;

// The "clamp and report" half of the tools' input contract. An out-of-range number is clamped rather than rejected (a
// model asking for 1000 rows gets the cap, not an error it has to recover from), and anything clamped, ignored or
// contradictory is listed in the result's `warnings`, so the model knows what it actually got. What cannot be clamped
// (a date that is not a date, an unknown sort key, an unknown tag path) stays an error.
public sealed class McpWarnings
{
    private readonly List<string> _items = [];

    public IReadOnlyList<string> Items => _items;

    public void Add(string warning) => _items.Add(warning);

    // The value within [min, max], with a warning when it had to move.
    public int Clamp(string name, int value, int min, int max)
    {
        if (value < min)
        {
            Add($"{name} {value} raised to {min}.");
            return min;
        }

        if (value > max)
        {
            Add($"{name} {value} lowered to {max}.");
            return max;
        }

        return value;
    }
}
