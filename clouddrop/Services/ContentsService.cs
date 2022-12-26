using System.Security.Claims;
using AutoMapper;
using clouddrop.Data;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace clouddrop.Services;

public class ContentsService : clouddrop.ContentsService.ContentsServiceBase
{
    private readonly DBC _dbc;
    private readonly IMapper _mapper;

    public ContentsService(DBC dbc, IMapper mapper)
    {
        _dbc = dbc;
        _mapper = mapper;
    }

    [Authorize]
    public override async Task<ContentsResponse> GetChildrenContents(GetChildrenContentsRequest request, ServerCallContext context)
    {
        var accessEmail = context.GetHttpContext().User.FindFirstValue(ClaimTypes.Email)!;
        
        var content = await _dbc.Contents
            .Include(v => v.Children)
            .Include(v => v.Parent)
            .Include(v => v.Storage)
            .FirstOrDefaultAsync(v => v.Id == request.ContentId);
        if (content == null)
            throw new RpcException(new Status(StatusCode.NotFound, "Parent content not found!"));

        var storage = await _dbc.Storages
            .Include(v => v.User)
            .FirstOrDefaultAsync(v => v.Id == content.Storage.Id);
        if (storage?.User.Email != accessEmail)
            throw new RpcException(new Status(StatusCode.PermissionDenied, "You dont have access to this storage!"));

        var children = content.Children.Select(v => new ContentMessage()
        {
            ContentType = (int)v.ContentType == 0 ? ContentTypeEnum.File : ContentTypeEnum.Folder,
            Id = v.Id,
            Name = v.Name,
            Parent = new ContentMessage() {Id = v.Parent.Id},
            Path = v.Path,
            Storage = new StorageMessage() {Id = v.Storage.Id}
        });
        return await Task.FromResult(new ContentsResponse() {Children = { children }});
    }

    [Authorize]
    public override async Task<ContentsResponse> GetContentsFromStorage(GetContentsFromStorageRequest request, ServerCallContext context)
    {
        var accessEmail = context.GetHttpContext().User.FindFirstValue(ClaimTypes.Email)!;

        var storage = await _dbc.Storages
            .Include(v => v.User)
            .Include(v => v.Contents)
            .FirstOrDefaultAsync(v => v.Id == request.StorageId);
        if (storage?.User.Email != accessEmail)
            throw new RpcException(new Status(StatusCode.PermissionDenied, "You dont have access to this storage!"));

        var contents = storage.Contents.Select(v => new ContentMessage()
        {
            ContentType = (int)v.ContentType == 0 ? ContentTypeEnum.File : ContentTypeEnum.Folder,
            Id = v.Id,
            Name = v.Name,
            Parent = new ContentMessage() {Id = v.Parent.Id},
            Path = v.Path,
            Storage = new StorageMessage() {Id = v.Storage.Id}
        });
        return await Task.FromResult(new ContentsResponse() {Children = { contents }});
    }
}