using Auth;
using AutoMapper;
using clouddrop.Models;
using Users;

namespace clouddrop.Data;

public class AppMappingProfile : Profile
{
    public AppMappingProfile()
    {			
        CreateMap<SignUpRequest, User>();
        CreateMap<SignInRequest, User>();
        CreateMap<User, UserProfileMessage>();
    }
}