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
        
        var plan = await _dbc.Plans.SingleOrDefaultAsync(v => v.Id == code.PlanId);
        if (plan == null)
            throw new RpcException(new Status(StatusCode.NotFound, "Plan not found!"));

        var nowUserPlan = await _dbc.Subscriptions
            .Include(v => v.User)
            .Include(v => v.Plan)
            .SingleOrDefaultAsync(v => v.User.Email == accessEmail);
        
        if (nowUserPlan != null)
        {
            if (nowUserPlan.Plan.Price > plan.Price)
                throw new RpcException(new Status(StatusCode.Unknown, "At the moment, you have a better plan"));
            
            if (nowUserPlan.Id == plan.Id)
            {
                nowUserPlan.FinishAt = DateTimeOffset
                    .FromUnixTimeSeconds(nowUserPlan.FinishAt)
                    .AddDays(30)
                    .ToUnixTimeSeconds();
                await _dbc.SaveChangesAsync();
                return await Task.FromResult(new MessageResult() { Message = "Ok" });
            }
            nowUserPlan.IsActive = false;
        }
        
        _dbc.Subscriptions.Add(new Subscription()
        {
            User = await _dbc.Users.SingleAsync(v => v.Email == accessEmail),
            Plan = plan,
            FinishAt = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds() // todo: finish at date in code
        });
        await _dbc.SaveChangesAsync();
        return await Task.FromResult(new MessageResult() { Message = "Ok" });
    }
}