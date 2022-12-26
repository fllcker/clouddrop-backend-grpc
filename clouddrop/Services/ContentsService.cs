using System.Security.Claims;
using AutoMapper;
using clouddrop.Data;
using clouddrop.Models;
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
    
    [Authorize]
    public override async Task<ContentMessage> NewContent(ContentMessage request, ServerCallContext context)
    {
        var storageId = request.Storage.Id;
        var storage = await _dbc.Storages
            .Include(v => v.User)
            .FirstOrDefaultAsync(v => v.Id == storageId);
        if (storage == null) throw new RpcException(new Status(StatusCode.NotFound, "Storage not found!"));
        if (storage.User.Email != context.GetHttpContext().User.FindFirstValue(ClaimTypes.Email)!)
            throw new RpcException(new Status(StatusCode.PermissionDenied, "No access to this storage!"));
        
        // check if file or folder with this name and path already exists
        if (await _dbc.Contents.Where(v => v.Path == request.Path).CountAsync(v => v.Name == request.Name) != 0)
            throw new RpcException(new Status(StatusCode.AlreadyExists, "File or folder with this name and path already exists"));

        var parent = await _dbc.Contents.FirstOrDefaultAsync(v => v.Id == request.Parent.Id);
        if (parent == null) throw new RpcException(new Status(StatusCode.NotFound, "Parent content not found!"));
        
        var newContent = new Content()
        {
            ContentType = request.ContentType == ContentTypeEnum.File ? ContentType.File : ContentType.Folder,
            Path = Path.Combine(parent.Path!, request.Name),
            Name = request.Name,
            Storage = new Storage() {Id = request.Storage.Id},
            Parent = request.Parent != null ? new Content() {Id = request.Parent.Id} : null
        };
        _dbc.Contents.Add(newContent);
        await _dbc.SaveChangesAsync();
        return await Task.FromResult(request);
    }

    [Authorize]
    public override async Task<ContentRemoveResult> RemoveContent(RemoveContentId request, ServerCallContext context)
    {
        var content = await _dbc.Contents
            .Include(v => v.Storage)
            .FirstOrDefaultAsync(v => v.Id == request.ContentId);
        if (content == null)
            throw new RpcException(new Status(StatusCode.NotFound, "Content not found!"));
        var storage = await _dbc.Storages
            .Include(v => v.User)
            .FirstOrDefaultAsync(v => v.Id == content.Storage.Id);
        if (storage?.User.Email != context.GetHttpContext().User.FindFirstValue(ClaimTypes.Email)!)
            throw new RpcException(new Status(StatusCode.PermissionDenied, "No access to this storage"));

        string contentPath = Path.Combine(Directory.GetCurrentDirectory(),
            "UsersStorage", $"storage{storage.Id}", content.Path ?? "unknown");
        if (content.ContentType == ContentType.File)
        {
            if (File.Exists(contentPath)) File.Delete(contentPath);
        }
        else
        {
            if (Directory.Exists(contentPath)) Directory.Delete(contentPath);
        }
        _dbc.Contents.Remove(content);
        await _dbc.SaveChangesAsync();
        return await Task.FromResult(new ContentRemoveResult() {Message = "Ok"});
    }
}