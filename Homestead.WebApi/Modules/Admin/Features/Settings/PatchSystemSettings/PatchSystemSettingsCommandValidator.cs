using FluentValidation;

namespace Homestead.WebApi.Modules.Admin.Features.Settings.PatchSystemSettings;

public class PatchSystemSettingsCommandValidator : AbstractValidator<PatchSystemSettingsCommand>
{
    public PatchSystemSettingsCommandValidator()
    {
        RuleFor(x => x.LogLevelOverrides)
            .Must(map => map is null || map.Keys.All(k => !string.IsNullOrWhiteSpace(k)))
            .WithMessage("Log level category names cannot be empty.")
            // The JsonStringEnumConverter binds arbitrary integers to an out-of-range LogLevel; reject them
            // here so nothing invalid is persisted or pushed into the live logger filters.
            .Must(map => map is null || map.Values.All(v => Enum.IsDefined(v)))
            .WithMessage("Log level must be one of: Trace, Debug, Information, Warning, Error, Critical, None.");
    }
}
