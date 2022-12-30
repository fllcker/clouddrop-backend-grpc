using System.Security.Claims;
using AutoMapper;
using clouddrop.Data;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace clouddrop.Services;

public class SubscriptionsService : clouddrop.SubscriptionsService.SubscriptionsServiceBase
{
    private readonly DBC _dbc;
    private readonly IMapper _mapper;

    public SubscriptionsService(DBC dbc, IMapper mapper)
    {
        _dbc = dbc;
        _mapper = mapper;
    }

    [Authorize]
    public override async Task<SubscriptionMessage> GetMySubscription(EmptyMessage request, ServerCallContext context)
    {
        var lastSubscription = await _dbc.Subscriptions
            .Include(v => v.User)
            .Include(v => v.Plan)
            .OrderBy(v => v.StartedAt)
            .LastOrDefaultAsync(v =>
                v.User.Email == context.GetHttpContext().User.FindFirstValue(ClaimTypes.Email));
        return await Task.FromResult(_mapper.Map<SubscriptionMessage>(lastSubscription));
    }
}