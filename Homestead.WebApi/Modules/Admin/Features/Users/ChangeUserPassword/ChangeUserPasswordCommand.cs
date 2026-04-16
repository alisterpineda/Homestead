using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.Users.ChangeUserPassword;

public record ChangeUserPasswordRequest
{
    public required string Password { get; init; }
}

public record ChangeUserPasswordCommand : IRequest<bool>
{
    public Guid Id { get; init; }
    public required string Password { get; init; }
}
