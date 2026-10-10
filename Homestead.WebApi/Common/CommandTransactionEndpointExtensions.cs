namespace Homestead.WebApi.Common;

/// <summary>
/// Marks an endpoint as wrapped by <see cref="CommandTransactionFilter"/>. The filter does the work; this
/// metadata exists so the coverage guard test can see which endpoints carry it.
/// </summary>
public sealed record CommandTransactionMetadata;

public static class CommandTransactionEndpointExtensions
{
    /// <summary>
    /// Runs every mutating request (anything but GET/HEAD/OPTIONS) of the endpoint, or of every endpoint in
    /// the group and its nested groups, inside one command transaction. Call it once on each module's
    /// top-level route group.
    /// </summary>
    public static TBuilder WithCommandTransaction<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.WithMetadata(new CommandTransactionMetadata());
        return builder.AddEndpointFilter<TBuilder, CommandTransactionFilter>();
    }
}
