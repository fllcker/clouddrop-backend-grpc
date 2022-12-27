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
    public override async Task<ContentMessage> NewFolder(NewFolderMessage request, ServerCallContext context)
    {
        Content? parent = null!;
        Storage? storage = null!;
        parent = await _dbc.Contents
            .Include(v => v.Storage)
            .Include(v => v.Storage.User)
            .FirstOrDefaultAsync(v => v.Id == request.ParentId);
        
        if (parent == null)
        {
            storage = await _dbc.Storages
                .Include(v => v.User)
                .FirstOrDefaultAsync(v => v.Id == request.StorageId);
        }
        else
        {
            storage = parent.Storage;
        }
        
        if (storage == null) throw new RpcException(new Status(StatusCode.NotFound, "Storage not found!"));
        if (storage.User.Email != context.GetHttpContext().User.FindFirstValue(ClaimTypes.Email)!)
            throw new RpcException(new Status(StatusCode.PermissionDenied, "No access to this storage!"));
        
        
        var totalPath = parent != null ? Path.Combine(parent.Path!, request.Name) : Path.Combine("home", request.Name);
        
        // check if file or folder with this name and path already exists
        if (await _dbc.Contents.Where(v => v.Path == totalPath).CountAsync(v => v.Name == request.Name) != 0)
            throw new RpcException(new Status(StatusCode.AlreadyExists, "File or folder with this name and path already exists"));

        if (request.Name == "home" || request.Name == "trashcan")
            throw new RpcException(new Status(StatusCode.Cancelled, "You cannot create a folder with this name"));
        
        var newContent = new Content()
        {
            ContentType = ContentType.Folder,
            Path = totalPath,
            Name = request.Name,
            StorageId = storage.Id,
            Parent = parent ?? null
        };
        _dbc.Contents.Add(newContent);
        await _dbc.SaveChangesAsync();
        return await Task.FromResult(new ContentMessage() // TODO
        {
            Id = newContent.Id,
            Path = newContent.Path,
            Name = newContent.Name
        });
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
        else if (content.ContentType == ContentType.Folder)
        {
            var removedSize = await DeleteContentRecursion(content);
            if (Directory.Exists(contentPath)) Directory.Delete(contentPath, recursive: true);
            if (removedSize != null) storage.StorageUsed -= (long)removedSize;
        }

        if (content.Size != null) storage.StorageUsed -= (long)content.Size;
        if (await _dbc.Contents.FindAsync(content.Id) != null) _dbc.Contents.Remove(content);
        await _dbc.SaveChangesAsync();
        return await Task.FromResult(new ContentRemoveResult() {Message = "Ok"});
    }

    private async Task<long?> DeleteContentRecursion(Content content, long? totalSize = null)
    {
        if (totalSize == null) totalSize = 0;
        var children = await _dbc.Contents
            .Include(v => v.Parent)
            .Where(v => v.Parent != null)
            .Where(v => v.Parent!.Id == content.Id)
            .ToListAsync();
        
        foreach (var child in children)
        {
            await DeleteContentRecursion(child);
        }

        if (content.Size != null) totalSize += content.Size;
        if (await _dbc.Contents.FindAsync(content.Id) != null) _dbc.Contents.Remove(content);
        await _dbc.SaveChangesAsync();
        return totalSize;
    }

    [Authorize]
    public override async Task<GetSpecialContentIdResponse> GetSpecialContentId(GetSpecialContentIdRequest request, ServerCallContext context)
    {
        var storage = await _dbc.Storages
            .Include(v => v.User)
            .Include(v => v.Contents)
            .SingleOrDefaultAsync(v => v.User.Email == context.GetHttpContext().User.FindFirstValue(ClaimTypes.Email)!);
        if (storage == null)
            throw new RpcException(new Status(StatusCode.NotFound, "User storage not found!"));

        Content? content = null;
        if (request.SpecialContentEnum == GetSpecialContentIdEnum.Home)
            content = storage.Contents.FirstOrDefault(v => v.Path == "home");
        if (request.SpecialContentEnum == GetSpecialContentIdEnum.Trashcan)
            content = storage.Contents.FirstOrDefault(v => v.Path == "trashcan");
        
        if (content != null)
            return await Task.FromResult(new GetSpecialContentIdResponse() {ContentId = content.Id});
        throw new RpcException(new Status(StatusCode.NotFound, "Not found!"));
    }
}