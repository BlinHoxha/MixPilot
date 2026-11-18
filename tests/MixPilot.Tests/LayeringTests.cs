using Microsoft.AspNetCore.Mvc;
using MixPilot.Application.Abstractions.Services;
using MixPilot.Application.Services;
using MixPilot.Contracts;
using MixPilot.Domain;
using MixPilot.Infrastructure;
using MixPilot.Infrastructure.Persistence.Repositories;

namespace MixPilot.Tests;

public sealed class LayeringTests
{
    [Fact]
    public void Repositories_and_services_use_MixPilot_bases()
    {
        Assert.All(new[] { typeof(TrackRepository), typeof(CollectionRepository), typeof(DjProfileRepository), typeof(SetRepository) },
            type => Assert.Equal(typeof(MixPilotRepository<>), type.BaseType!.GetGenericTypeDefinition()));
        Assert.All(new[] { typeof(TrackService), typeof(CollectionService), typeof(DjProfileService), typeof(SetService) },
            type => Assert.Equal(typeof(MixPilotService<,>), type.BaseType!.GetGenericTypeDefinition()));
        Assert.Equal(typeof(Framework.Persistence.FrameworkDbContext), typeof(MixPilotDbContext).BaseType);
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
    public void Generic_service_writes_cannot_bypass_MixPilot_validation()
    {
        foreach (var service in new[] { typeof(TrackService), typeof(CollectionService), typeof(DjProfileService), typeof(SetService) })
        {
            Assert.DoesNotContain(service.GetInterfaces(), type => type.IsGenericType && type.GetGenericTypeDefinition().Name == "IBaseService`2");
            Assert.DoesNotContain(service.GetMethods(), method => method.IsGenericMethod &&
                new[] { "Add", "AddRange", "Update", "UpdateRange", "Remove", "RemoveRange" }.Contains(method.Name));
        }
    }

    [Fact]
    public void MixPilot_does_not_ship_AutoMapper_or_legacy_Framework_facades()
    {
        using var dependencies = System.Text.Json.JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "MixPilot.Tests.deps.json")));
        var libraries = dependencies.RootElement.GetProperty("libraries").EnumerateObject().Select(entry => entry.Name).ToArray();
        Assert.DoesNotContain(libraries, name => name.StartsWith("AutoMapper/", StringComparison.Ordinal));
        Assert.DoesNotContain(libraries, name => name.StartsWith("Framework.Infrastructure/", StringComparison.Ordinal));
        Assert.DoesNotContain(libraries, name => name.StartsWith("Framework.Application/", StringComparison.Ordinal));
    }
}
