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

    public UsersService(DBC dbc, IMapService mapService)
    {
        _dbc = dbc;
        _mapService = mapService;
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
    public override async Task<UsersEmptyMessage> UpdateProfileInfo(UserInfoMessage request, ServerCallContext context)
    {
        var email = context.GetHttpContext().User.FindFirstValue(ClaimTypes.Email)!;
        var user = await _dbc.Users
            .SingleOrDefaultAsync(v => v.Email == email);
        if (user == null)
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Unauthorized"));

        if (!String.IsNullOrEmpty(request.Name)) user.Name = request.Name;
        if (!String.IsNullOrEmpty(request.FirstName)) user.FirstName = request.FirstName;
        if (!String.IsNullOrEmpty(request.LastName)) user.LastName = request.LastName;
        if (!String.IsNullOrEmpty(request.Country)) user.Country = request.Country;
        if (!String.IsNullOrEmpty(request.City)) user.City = request.City;

        await _dbc.SaveChangesAsync();
        return await Task.FromResult(new UsersEmptyMessage());
    }
}