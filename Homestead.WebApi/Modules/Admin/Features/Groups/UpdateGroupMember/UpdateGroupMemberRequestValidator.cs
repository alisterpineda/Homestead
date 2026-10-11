using FluentValidation;

namespace Homestead.WebApi.Modules.Admin.Features.Groups.UpdateGroupMember;

public class UpdateGroupMemberRequestValidator : AbstractValidator<UpdateGroupMemberRequest>
{
    public UpdateGroupMemberRequestValidator()
    {
        RuleFor(x => x.AccessLevel)
            .IsInEnum()
            .WithMessage("AccessLevel must be a valid value.");
    }
}
