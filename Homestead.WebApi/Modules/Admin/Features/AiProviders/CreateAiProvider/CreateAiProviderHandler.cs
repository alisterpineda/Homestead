using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Features.AiProviders.CreateAiProvider;

public class CreateAiProviderHandler(AppDbContext db, ICurrentUserAccessor currentUser, IAuditContext audit)
    : IRequestHandler<CreateAiProviderCommand, AiProviderResponse>
{
    public async Task<AiProviderResponse> Handle(CreateAiProviderCommand request, CancellationToken ct)
    {
        var trimmedName = request.Name.Trim();

        if (string.IsNullOrWhiteSpace(trimmedName))
            throw new BusinessRuleException("Name is required.");

        var nameExists = await db.AiProviders
            .AnyAsync(e => e.Name == trimmedName, ct);

        if (nameExists)
            throw new DuplicateEntityException("An AI provider with this name already exists.");

        AiProviderConfigValidator.Validate(request.ProviderType, request.Configuration);

        var userId = currentUser.UserId;

        var provider = new AiProvider
        {
            Id = Guid.NewGuid(),
            Name = trimmedName,
            ProviderType = request.ProviderType,
            Configuration = request.Configuration,
            IsEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = userId
        };

        db.AiProviders.Add(provider);
        audit.Created("AiProvider", provider.Id);
        await db.SaveChangesAsync(ct);

        var creatorName = userId.HasValue
            ? await db.Users.Where(u => u.Id == userId.Value).Select(u => u.DisplayName).FirstOrDefaultAsync(ct)
            : null;

        return new AiProviderResponse
        {
            Id = provider.Id,
            Name = provider.Name,
            ProviderType = provider.ProviderType,
            Configuration = JsonSecretConfig.Mask(provider.Configuration, AiProviderSecrets.Keys),
            IsEnabled = provider.IsEnabled,
            CreatedAt = provider.CreatedAt,
            UpdatedAt = provider.UpdatedAt,
            CreatedByUserId = provider.CreatedByUserId,
            CreatedByUserName = creatorName,
        };
    }
}
