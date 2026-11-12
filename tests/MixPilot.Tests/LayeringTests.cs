using AutoMapper;
using Framework.Application.Abstractions.Services;
using Framework.Application.Services;
using Framework.Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using MixPilot.Application.Abstractions.Services;
using MixPilot.Application.Services;
using MixPilot.Contracts;
using MixPilot.Domain;
using MixPilot.Infrastructure;
using MixPilot.Infrastructure.Persistence.Repositories;

namespace MixPilot.Tests;

public sealed class LayeringTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public void Repositories_and_services_reuse_Framework_bases()
    {
        Assert.True(typeof(BaseRepository<Track, Guid, MixPilotDbContext>).IsAssignableFrom(typeof(TrackRepository)));
        Assert.True(typeof(BaseRepository<Collection, Guid, MixPilotDbContext>).IsAssignableFrom(typeof(CollectionRepository)));
        Assert.True(typeof(BaseRepository<DjProfile, Guid, MixPilotDbContext>).IsAssignableFrom(typeof(DjProfileRepository)));
        Assert.True(typeof(BaseRepository<DjSet, Guid, MixPilotDbContext>).IsAssignableFrom(typeof(SetRepository)));
        Assert.True(typeof(BaseService<Track, Guid>).IsAssignableFrom(typeof(TrackService)));
        Assert.True(typeof(BaseService<Collection, Guid>).IsAssignableFrom(typeof(CollectionService)));
        Assert.True(typeof(BaseService<DjProfile, Guid>).IsAssignableFrom(typeof(DjProfileService)));
        Assert.True(typeof(BaseService<DjSet, Guid>).IsAssignableFrom(typeof(SetService)));
    }

    [Fact]
    public void Controllers_depend_on_focused_service_interfaces()
    {
        var controllers = typeof(Api.Program).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(ControllerBase)));
        foreach (var controller in controllers)
        foreach (var parameter in controller.GetConstructors().SelectMany(c => c.GetParameters()))
        {
            if (controller.Name == "CapabilitiesController") continue;
            Assert.True(parameter.ParameterType.IsInterface);
            Assert.Equal(typeof(ITrackService).Namespace, parameter.ParameterType.Namespace);
        }
    }

    [Fact]
    public async Task Generic_service_writes_cannot_bypass_MixPilot_validation()
    {
        using var client = factory.Client("generic-write-test");
        using var scope = factory.Services.CreateScope();
        var service = (IBaseService<Track, Guid>)scope.ServiceProvider.GetRequiredService<ITrackService>();
        var create = await Assert.ThrowsAsync<RuleViolation>(() => service.Add(new CreateTrackRequest { Title = "Bypass" }));
        Assert.Equal("explicit_operation_required", create.Code);
        Assert.Equal("explicit_operation_required", (await Assert.ThrowsAsync<RuleViolation>(() => service.Remove(Guid.NewGuid()))).Code);
        Assert.Equal("explicit_operation_required", (await Assert.ThrowsAsync<RuleViolation>(() => service.AddRange(new[] { new CreateTrackRequest() }))).Code);
        scope.ServiceProvider.GetRequiredService<IMapper>().ConfigurationProvider.AssertConfigurationIsValid();
    }
}
