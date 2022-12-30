using System.Security.Claims;
using clouddrop.Data;
using clouddrop.Models;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace clouddrop.Services;

public class CodesService : clouddrop.CodesService.CodesServiceBase
{
    private readonly DBC _dbc;

    public CodesService(DBC dbc)
    {
        _dbc = dbc;
    }

    [Authorize]
    public override async Task<MessageResult> Activate(ActiveCodeMessage request, ServerCallContext context)
    {
        var accessEmail = context.GetHttpContext().User.FindFirstValue(ClaimTypes.Email)!;

        var code = await _dbc.PurchaseCodes.SingleOrDefaultAsync(v => v.SecretNumber == request.Code);
        if (code == null)
            throw new RpcException(new Status(StatusCode.NotFound, "Code not found!"));

        if (code.Activations >= code.MaxActivations)
            throw new RpcException(new Status(StatusCode.NotFound, "Activation limit exceeded!"));
        
        var plan = await _dbc.Plans.SingleOrDefaultAsync(v => v.Id == code.PlanId);
        if (plan == null)
            throw new RpcException(new Status(StatusCode.NotFound, "Plan not found!"));

        var nowUserPlan = await _dbc.Subscriptions
            .Include(v => v.User)
            .Include(v => v.Plan)
            .OrderBy(v => v.StartedAt)
            .LastOrDefaultAsync(v => v.User.Email == accessEmail);

        if (nowUserPlan != null)
        {
            if (nowUserPlan.Plan.Price > plan.Price)
                throw new RpcException(new Status(StatusCode.Unknown, "At the moment, you have a better plan"));
            
            nowUserPlan.IsActive = false;
        }

        var user = await _dbc.Users.SingleAsync(v => v.Email == accessEmail);
        var storage = await _dbc.Storages
            .Include(v => v.User)
            .SingleOrDefaultAsync(v => v.User.Id == user.Id);
        if (storage == null)
            throw new RpcException(new Status(StatusCode.NotFound, "User storage not found!"));
        storage.StorageQuote = plan.AvailableQuote;
        
        _dbc.Subscriptions.Add(new Subscription()
        {
            User = user,
            Plan = plan,
            FinishAt = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds() // todo: finish at date in code
        });

        code.Activations += 1;
        await _dbc.SaveChangesAsync();
        return await Task.FromResult(new MessageResult() { Message = "Ok" });
    }
}