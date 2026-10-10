using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Documents.Processing;
using Microsoft.Extensions.DependencyInjection;
using DocumentsPatchTagHandler = Homestead.WebApi.Modules.Documents.Features.Tags.PatchTag.PatchTagHandler;

namespace Homestead.WebApi.Tests.Unit.Common;

public class HandlerServiceCollectionExtensionsTests
{
    private static readonly List<ServiceDescriptor> Registrations = new ServiceCollection().AddHandlers().ToList();

    [Fact]
    public void AddHandlers_FindsEveryFeatureHandler()
    {
        // ~165 feature handlers at the time the scan was introduced; a count far below that means the
        // convention stopped matching.
        Assert.True(Registrations.Count > 150, $"Expected > 150 handlers, found {Registrations.Count}.");
    }

    [Fact]
    public void AddHandlers_RegistersConcreteTypeAsScoped()
    {
        var registration = Assert.Single(Registrations, d => d.ServiceType == typeof(DocumentsPatchTagHandler));

        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
        Assert.Equal(typeof(DocumentsPatchTagHandler), registration.ImplementationType);
    }

    [Fact]
    public void AddHandlers_SkipsJobHandlersAndHandlersOutsideModules()
    {
        Assert.DoesNotContain(Registrations, d => d.ServiceType == typeof(UploadDocumentJobHandler));
        Assert.DoesNotContain(Registrations, d => d.ServiceType == typeof(DomainExceptionHandler));
        Assert.DoesNotContain(Registrations, d => d.ServiceType == typeof(DocumentAuthorizationHandler));
    }
}
