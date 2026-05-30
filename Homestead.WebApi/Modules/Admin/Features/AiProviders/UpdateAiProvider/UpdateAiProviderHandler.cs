using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Features.AiProviders.UpdateAiProvider;

public class UpdateAiProviderHandler(AppDbContext db, IAuditContext audit, IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateAiProviderCommand, AiProviderResponse>
{
    public async Task<AiProviderResponse> Handle(UpdateAiProviderCommand command, CancellationToken ct)
    {
        var provider = await db.AiProviders
            .Include(e => e.CreatedByUser)
            .FirstOrDefaultAsync(e => e.Id == command.Id, ct);

        if (provider is null)
            throw new EntityNotFoundException("AI provider not found.");

        var trimmedName = command.Request.Name.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
            throw new BusinessRuleException("Name is required.");

        var nameConflict = await db.AiProviders
            .AnyAsync(e => e.Id != command.Id && e.Name == trimmedName, ct);

        if (nameConflict)
            throw new DuplicateEntityException("An AI provider with this name already exists.");

        // Merge so a blank/masked-sentinel apiKey reuses the stored value, then validate the
        // merged config (the version we actually persist). Snapshot diffing runs on raw config.
        var merged = string.IsNullOrWhiteSpace(command.Request.Configuration)
            ? command.Request.Configuration
            : JsonSecretConfig.Merge(command.Request.Configuration, provider.Configuration, AiProviderSecrets.Keys);

        AiProviderConfigValidator.Validate(provider.ProviderType, merged);

        audit.Track<AiProviderSnapshot, AiProvider>(provider, "AiProvider", provider.Id);

        provider.Name = trimmedName;
        provider.Configuration = merged;
        provider.IsEnabled = command.Request.IsEnabled;
        provider.UpdatedAt = DateTimeOffset.UtcNow;

        await unitOfWork.SaveChangesAsync(ct);

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
            CreatedByUserName = provider.CreatedByUser?.DisplayName,
        };
    }
}

public record UpdateAiProviderCommand : ICommand<AiProviderResponse>
{
    public Guid Id { get; init; }
    public required UpdateAiProviderRequest Request { get; init; }
}
