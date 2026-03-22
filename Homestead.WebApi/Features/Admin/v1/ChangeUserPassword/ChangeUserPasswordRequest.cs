using MediatR;

namespace Homestead.WebApi.Features.Admin.v1.ChangeUserPassword;

public record ChangeUserPasswordRequest
{
    public required string Password { get; init; }
}

public record ChangeUserPasswordCommand : IRequest<bool>
{
    public Guid Id { get; init; }
    public required string Password { get; init; }
}
