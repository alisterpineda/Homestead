using FluentValidation;

namespace Homestead.WebApi.Modules.Admin.Features.Groups.AddGroupMember;

public class AddGroupMemberRequestValidator : AbstractValidator<AddGroupMemberRequest>
{
    public AddGroupMemberRequestValidator()
    {
        RuleFor(x => x.UserId)
            .NotEqual(Guid.Empty)
            .WithMessage("UserId is required.");

        RuleFor(x => x.AccessLevel)
            .IsInEnum()
            .WithMessage("AccessLevel must be a valid value.");
    }
}
