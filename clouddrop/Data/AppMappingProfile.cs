using AutoMapper;
using clouddrop.Models;

namespace clouddrop.Data;

public class AppMappingProfile : Profile
{
    public AppMappingProfile()
    {			
        CreateMap<SignUpRequest, User>();
        CreateMap<SignInRequest, User>();
        CreateMap<User, UserProfileMessage>();
        CreateMap<Content, ContentMessage>();
        CreateMap<Plan, PlanMessage>();
    }
}