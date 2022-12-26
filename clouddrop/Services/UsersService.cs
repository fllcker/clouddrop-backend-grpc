using System.Security.Claims;
using AutoMapper;
using clouddrop.Data;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace clouddrop.Services;

public class UsersService : clouddrop.UsersService.UsersServiceBase
{
    
    private readonly DBC _dbc;
    private readonly IMapper _mapper;

    public UsersService(DBC dbc, IMapper mapper)
    {
        _dbc = dbc;
        _mapper = mapper;
    }

    public override async Task<UserProfileMessage> GetUserById(UserByIdRequest request, ServerCallContext context)
    {
        var user = await _dbc.Users
            .Include(v => v.Storage)
            .SingleOrDefaultAsync(v => v.Id == request.Id);
        if (user == null)
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Unauthorized"));
        return await Task.FromResult(_mapper.Map<UserProfileMessage>(user));
    }
    
    [Authorize]
    public override async Task<UserProfileMessage> GetProfile(UsersEmptyMessage request, ServerCallContext context)
    {
        var email = context.GetHttpContext().User.FindFirstValue(ClaimTypes.Email)!;
        var user = await _dbc.Users
            .Include(v => v.Storage)
            .SingleOrDefaultAsync(v => v.Email == email);
        if (user == null)
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Unauthorized"));
        return await Task.FromResult(_mapper.Map<UserProfileMessage>(user));
    }
}