using AutoMapper;
using AIFramework.Core.Abstractions;
using AIFramework.Application.Services;
using AIFramework.Local;
using AIFramework.Azure;
using AIFramework.Aws;
using MixPilot.Application.AI;
using MixPilot.Infrastructure.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MixPilot.Application.Abstractions.Persistence;
using MixPilot.Application.Abstractions.Services;
using MixPilot.Application.Mapping;
using MixPilot.Application.Services;
using MixPilot.Domain;
using MixPilot.Infrastructure.Persistence.Repositories;

namespace MixPilot.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddMixPilot(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddAutoMapper(typeof(MixPilotMappingProfile).Assembly);
        services.AddScoped<ITrackRepository, TrackRepository>();
        services.AddScoped<ICollectionRepository, CollectionRepository>();
        services.AddScoped<IDjProfileRepository, DjProfileRepository>();
        services.AddScoped<ISetRepository, SetRepository>();
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddScoped<ITrackService, TrackService>();
        services.AddScoped<ICollectionService, CollectionService>();
        services.AddScoped<IDjProfileService, DjProfileService>();
        services.AddScoped<ISetService, SetService>();
        services.AddSingleton<DjEngine>();
        bool aiEnabled = bool.TryParse(configuration["AI:Enabled"], out var enabled) && enabled;
        services.AddSingleton(new AiFeatureOptions(aiEnabled));
        if (aiEnabled)
        {
            switch (configuration["AI:Provider"] ?? "Local")
            {
                case "Local": services.AddLocalAi(); break;
                case "Azure": services.AddAzureAi(configuration); break;
                case "Aws": services.AddBedrockChat(new(configuration["AI:Aws:Region"] ?? "", configuration["AI:Aws:ModelId"] ?? "")); break;
                default: throw new InvalidOperationException("Unsupported AI provider.");
            }
        }
        else services.AddSingleton<IChatModel, UnavailableChatModel>();
        services.AddScoped<SetExplanationEvidence>();
        services.AddScoped<IKnowledgeRetriever>(provider => provider.GetRequiredService<SetExplanationEvidence>());
        services.AddSingleton<IDocumentProfileCatalog, SetDocumentProfileCatalog>();
        services.AddScoped<KnowledgeAnswerService>();
        services.AddScoped<ISetExplanationService, SetExplanationService>();
        services.AddDbContext<MixPilotDbContext>(options => options.UseNpgsql(
            configuration.GetConnectionString("MixPilot") ?? throw new InvalidOperationException("ConnectionStrings:MixPilot is required.")));
        return services;
    }
}

