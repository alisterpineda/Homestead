using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Settings;
using Homestead.WebApi.Features.Auditing;
using MediatR;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Admin.v1.TextExtractionEngines.CreateTextExtractionEngine;

public class CreateTextExtractionEngineHandler(AppDbContext db, ICurrentUserAccessor currentUser, IAuditContext audit)
    : IRequestHandler<CreateTextExtractionEngineCommand, TextExtractionEngineResponse>
{
    public async Task<TextExtractionEngineResponse> Handle(CreateTextExtractionEngineCommand request, CancellationToken ct)
    {
        var trimmedName = request.Name.Trim();

        if (string.Equals(trimmedName, BuiltInTextExtractionEngines.TesseractDefaultName, StringComparison.OrdinalIgnoreCase))
            throw new DuplicateEntityException("A text extraction engine with this name already exists.");

        var nameExists = await db.TextExtractionEngines
            .AnyAsync(e => e.Name == trimmedName, ct);

        if (nameExists)
            throw new DuplicateEntityException("A text extraction engine with this name already exists.");

        TextExtractionEngineConfigValidator.Validate(request.EngineType, request.Configuration);

        var userId = currentUser.UserId;

        var engine = new TextExtractionEngine
        {
            Id = Guid.NewGuid(),
            Name = trimmedName,
            EngineType = request.EngineType,
            Configuration = request.Configuration,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = userId
        };

        db.TextExtractionEngines.Add(engine);
        audit.Created("TextExtractionEngine", engine.Id);
        await db.SaveChangesAsync(ct);

        var creatorName = userId.HasValue
            ? await db.Users.Where(u => u.Id == userId.Value).Select(u => u.DisplayName).FirstOrDefaultAsync(ct)
            : null;

        return new TextExtractionEngineResponse
        {
            Id = engine.Id,
            Name = engine.Name,
            EngineType = engine.EngineType,
            Configuration = engine.Configuration,
            IsBuiltIn = false,
            CreatedAt = engine.CreatedAt,
            CreatedByUserId = engine.CreatedByUserId,
            CreatedByUserName = creatorName,
        };
    }
}
