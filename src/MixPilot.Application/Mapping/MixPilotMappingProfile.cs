using AutoMapper;
using MixPilot.Domain;

namespace MixPilot.Application.Mapping;

public sealed class MixPilotMappingProfile : Profile
{
    public MixPilotMappingProfile()
    {
        // Validated aggregates are already mapped by application use cases.
        // Preserve their identity and revision when Framework.BaseService persists them.
        CreateMap<Track, Track>().ConvertUsing(source => source);
        CreateMap<Collection, Collection>().ConvertUsing(source => source);
        CreateMap<DjProfile, DjProfile>().ConvertUsing(source => source);
        CreateMap<DjSet, DjSet>().ConvertUsing(source => source);
    }
}

