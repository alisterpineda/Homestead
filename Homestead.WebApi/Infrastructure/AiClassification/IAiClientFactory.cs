using Homestead.WebApi.Domain.Settings;
using Microsoft.Extensions.AI;

namespace Homestead.WebApi.Infrastructure.AiClassification;

public interface IAiClientFactory
{
    IChatClient CreateClient(AiProvider provider);
}
