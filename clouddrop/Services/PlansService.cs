using AutoMapper;
using clouddrop.Data;
using clouddrop.Services.Other;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace clouddrop.Services;

public class PlansService : clouddrop.PlansService.PlansServiceBase
{
    private readonly DBC _dbc;
    private readonly IMapper _mapper;
    private IMemoryCache _cache;

    public PlansService(DBC dbc, IMapper mapper, IMemoryCache cache)
    {
        _dbc = dbc;
        _mapper = mapper;
        _cache = cache;
    }

    public override async Task<PlansMessage> GetAll(GetAllRequest request, ServerCallContext context)
    {
        int take = request.Max ?? 3;
        if (!_cache.TryGetValue(CacheKeys.Plans, out List<PlanMessage>? cachePlans))
        {
            cachePlans = await _dbc.Plans
                .Take(take)
                .Select(v => _mapper.Map<PlanMessage>(v))
                .ToListAsync();

            _cache.Set(CacheKeys.Plans, cachePlans);
        }

        return await Task.FromResult(new PlansMessage() { Plans = { cachePlans!.Take(take) } });
    }
}