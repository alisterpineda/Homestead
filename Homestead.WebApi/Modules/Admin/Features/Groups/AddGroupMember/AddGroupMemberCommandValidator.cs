using FluentValidation;

namespace Homestead.WebApi.Modules.Admin.Features.Groups.AddGroupMember;

public class AddGroupMemberCommandValidator : AbstractValidator<AddGroupMemberCommand>
{
    public AddGroupMemberCommandValidator()
    {
        RuleFor(x => x.Request.UserId)
            .NotEqual(Guid.Empty)
            .WithMessage("UserId is required.");

        RuleFor(x => x.Request.AccessLevel)
            .IsInEnum()
            .WithMessage("AccessLevel must be a valid value.");
    }
}
