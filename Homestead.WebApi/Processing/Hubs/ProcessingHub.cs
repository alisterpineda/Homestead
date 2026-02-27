using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Homestead.WebApi.Processing.Hubs;

[Authorize]
public class ProcessingHub : Hub<IProcessingClient>
{
}
