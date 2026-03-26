using FluentValidation;

namespace Homestead.WebApi.Features.Admin.Groups.UpdateGroupMember;

public class UpdateGroupMemberCommandValidator : AbstractValidator<UpdateGroupMemberCommand>
{
    public UpdateGroupMemberCommandValidator()
    {
        RuleFor(x => x.Request.AccessLevel)
            .IsInEnum()
            .WithMessage("AccessLevel must be a valid value.");
    }
}
