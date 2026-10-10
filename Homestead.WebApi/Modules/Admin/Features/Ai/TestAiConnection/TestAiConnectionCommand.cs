namespace Homestead.WebApi.Modules.Admin.Features.Ai.TestAiConnection;

// Deliberately write-free: the endpoint is a POST, so the command transaction filter wraps it, but the
// transaction opens lazily on the first flush with work. A write here would open it and pin a pooled
// connection for the duration of the slow external call (the idle-in-transaction shape the lazy-tx
// work fights).
//
// ProviderKey null = probe whatever the resolution chain picks (Ai:DefaultProvider / sole provider);
// a key probes that specific provider, so a new one can be verified before the default is flipped to it.
public record TestAiConnectionCommand(string? ProviderKey = null);
