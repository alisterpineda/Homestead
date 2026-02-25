using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace HomeSuiteHome.WebApi.Processing.Hubs;

[Authorize]
public class ProcessingHub : Hub<IProcessingClient>
{
}
