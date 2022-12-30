using AutoMapper;
using clouddrop.Data;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace clouddrop.Services;

public class PlansService : clouddrop.PlansService.PlansServiceBase
{
    private readonly DBC _dbc;
    private readonly IMapper _mapper;

    public PlansService(DBC dbc, IMapper mapper)
    {
        _dbc = dbc;
        _mapper = mapper;
    }

    public override async Task<PlansMessage> GetAll(GetAllRequest request, ServerCallContext context)
    {
        int take = request.Max ?? 3;
        var plans = await _dbc.Plans
            .Take(take)
            .Select(v => _mapper.Map<PlanMessage>(v))
            .ToListAsync();
        return await Task.FromResult(new PlansMessage() { Plans = { plans } });
    }
}