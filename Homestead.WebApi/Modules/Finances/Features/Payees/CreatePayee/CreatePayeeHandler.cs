using FluentValidation;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;

namespace Homestead.WebApi.Modules.Finances.Features.Payees.CreatePayee;

public class CreatePayeeHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IAuditContext audit,
    IUnitOfWork unitOfWork,
    IValidator<CreatePayeeCommand> validator)
{
    public async Task<PayeeResponse> HandleAsync(CreatePayeeCommand request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);

        var name = request.Name.Trim();

        Account? defaultCategory = null;
        if (request.DefaultCategoryId is Guid categoryId)
            defaultCategory = await PayeeWriteHelpers.ValidateDefaultCategoryAsync(db, categoryId, ct);

        if (await PayeeWriteHelpers.NameTakenAsync(db, name, null, ct))
            throw new DuplicateEntityException($"A payee named '{name}' already exists.");

        var payee = new Payee
        {
            Name = name,
            DefaultCategoryId = request.DefaultCategoryId,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            IsArchived = false,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = currentUser.UserId,
            UpdatedByUserId = currentUser.UserId,
        };

        db.Payees.Add(payee);
        audit.Created("Payee", payee.Id);
        await unitOfWork.SaveChangesAsync(ct);

        return PayeeResponseMapper.ToResponse(payee, 0, defaultCategory);
    }
}
