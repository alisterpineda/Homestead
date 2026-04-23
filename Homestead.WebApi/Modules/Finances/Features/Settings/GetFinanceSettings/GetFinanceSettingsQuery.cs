using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Settings.GetFinanceSettings;

public record GetFinanceSettingsQuery : IRequest<FinanceSettingsResponse>;
