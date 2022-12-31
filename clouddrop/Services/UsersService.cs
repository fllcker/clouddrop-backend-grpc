using System.Security.Claims;
using AutoMapper;
using clouddrop.Data;
using clouddrop.Models;
using clouddrop.Services.Other;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace clouddrop.Services;

public class UsersService : clouddrop.UsersService.UsersServiceBase
{
    
    private readonly DBC _dbc;
    private readonly IMapService _mapService;
    private readonly IMapper _mapper;

    public UsersService(DBC dbc, IMapService mapService, IMapper mapper)
    {
        _dbc = dbc;
        _mapService = mapService;
        _mapper = mapper;
    }

    public override async Task<UserProfileMessage> GetUserById(UserByIdRequest request, ServerCallContext context)
    {
        var user = await _dbc.Users
            .Include(v => v.Storage)
            .SingleOrDefaultAsync(v => v.Id == request.Id);
        if (user == null)
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Unauthorized"));
        return await Task.FromResult(_mapService.Map<User, UserProfileMessage>(user));
    }
    
    [Authorize]
    public override async Task<UserProfileMessage> GetProfile(UsersEmptyMessage request, ServerCallContext context)
    {
        var email = context.GetHttpContext().User.FindFirstValue(ClaimTypes.Email)!;
        var user = await _dbc.Users
            .Include(v => v.Storage)
            .Include(v => v.Subscription)
            .SingleOrDefaultAsync(v => v.Email == email);
        if (user == null)
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Unauthorized"));
        return await Task.FromResult(_mapService.Map<User, UserProfileMessage>(user));
    }

    [Authorize]
    public override async Task<UserInfoMessage> GetUserInfo(UsersEmptyMessage request, ServerCallContext context)
    {
        var userInfo = await _dbc.Users
            .OfType<UserInfo>()
            .SingleOrDefaultAsync(v => v.Email == context.GetHttpContext().User.FindFirstValue(ClaimTypes.Email));
        if (userInfo == null)
            throw new RpcException(new Status(StatusCode.NotFound, "User info not found!"));
        return await Task.FromResult(_mapper.Map<UserInfoMessage>(userInfo));
    }

    [Authorize]
    public override async Task<UsersEmptyMessage> UpdateUserInfo(UserInfoMessage request, ServerCallContext context)
    {
        var userInfo = await _dbc.Users
            .OfType<UserInfo>()
            .SingleOrDefaultAsync(v => v.Email == context.GetHttpContext().User.FindFirstValue(ClaimTypes.Email));
        if (userInfo == null)
            throw new RpcException(new Status(StatusCode.NotFound, "User info not found!"));
        var newUserInfo = _mapper.Map<UserInfoMessage>(request);

        userInfo.FirstName = newUserInfo.FirstName ?? userInfo.FirstName;
        userInfo.LastName = newUserInfo.LastName ?? userInfo.LastName;
        userInfo.City = newUserInfo.City ?? userInfo.City;
        userInfo.Country = newUserInfo.Country ?? userInfo.Country;
        await _dbc.SaveChangesAsync();
        
        return await Task.FromResult<UsersEmptyMessage>(new UsersEmptyMessage());
    }
}