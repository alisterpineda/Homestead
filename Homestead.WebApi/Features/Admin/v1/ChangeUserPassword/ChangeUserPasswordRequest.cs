namespace Homestead.WebApi.Features.Admin.v1.ChangeUserPassword;

public record ChangeUserPasswordRequest
{
    public required string Password { get; init; }
}

public record ChangeUserPasswordCommand
{
    public Guid Id { get; init; }
    public required string Password { get; init; }
}
