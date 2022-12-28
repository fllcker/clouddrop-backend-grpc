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
    public override async Task<ContentsResponse> GetChildrenContents(GetChildrenContentsRequest request,
        ServerCallContext context)
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

        var children = content.Children
            .Where(v => v.IsDeleted == false)
            .Select(v => new ContentMessage()
            {
                ContentType = (int)v.ContentType == 0 ? ContentTypeEnum.File : ContentTypeEnum.Folder,
                Id = v.Id,
                Name = v.Name,
                Size = v.Size,
                Parent = new ContentMessage() { Id = v.Parent.Id },
                Path = v.Path,
                Storage = new StorageMessage() { Id = v.Storage.Id }
            });

        ContentsResponse cr = null!;
        if (request.ContentSort == ContentSort.Name)
            cr = new ContentsResponse() { Children = { children.OrderBy(v => v.Name) } };
        else if (request.ContentSort == ContentSort.Size)
            cr = new ContentsResponse() { Children = { children.OrderBy(v => v.Size) } };
        else
            cr = new ContentsResponse() { Children = { children.OrderByDescending(v => v.CreatedAt) } };
        return await Task.FromResult(cr);
    }

    [Authorize]
    public override async Task<ContentsResponse> GetContentsFromStorage(GetContentsFromStorageRequest request,
        ServerCallContext context)
    {
        var accessEmail = context.GetHttpContext().User.FindFirstValue(ClaimTypes.Email)!;

        var storage = await _dbc.Storages
            .Include(v => v.User)
            .Include(v => v.Contents)
            .FirstOrDefaultAsync(v => v.Id == request.StorageId);
        if (storage?.User.Email != accessEmail)
            throw new RpcException(new Status(StatusCode.PermissionDenied, "You dont have access to this storage!"));

        var contents = storage.Contents
            .Where(v => v.IsDeleted == false)
            .Select(v => new ContentMessage()
            {
                ContentType = (int)v.ContentType == 0 ? ContentTypeEnum.File : ContentTypeEnum.Folder,
                Id = v.Id,
                Name = v.Name,
                Size = v.Size,
                Parent = new ContentMessage() { Id = v.Parent.Id },
                Path = v.Path,
                Storage = new StorageMessage() { Id = v.Storage.Id }
            });
        return await Task.FromResult(new ContentsResponse() { Children = { contents } });
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
        if (await _dbc.Contents
                .Where(v => v.IsDeleted == false)
                .Where(v => v.Path == totalPath).CountAsync(v => v.Name == request.Name) != 0)
            throw new RpcException(new Status(StatusCode.AlreadyExists,
                "File or folder with this name and path already exists"));

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
            if (request.Full == true && File.Exists(contentPath)) File.Delete(contentPath);
        }
        else if (content.ContentType == ContentType.Folder)
        {
            var removedSize = await DeleteContentRecursion(content, full: request.Full);
            if (request.Full == true && Directory.Exists(contentPath)) Directory.Delete(contentPath, recursive: true);
            if (removedSize != null) storage.StorageUsed -= (long)removedSize;
        }

        if (content.Size != null) storage.StorageUsed -= (long)content.Size;
        // soft delete
        content.IsDeleted = true;
        _dbc.Contents.Update(content);
        if (request.Full == true && await _dbc.Contents.FindAsync(content.Id) != null) _dbc.Contents.Remove(content);
        await _dbc.SaveChangesAsync();

        return await Task.FromResult(new ContentRemoveResult() { Message = "Ok" });
    }

    private async Task<long?> DeleteContentRecursion(Content content, long? totalSize = null, bool? full = false)
    {
        totalSize ??= 0;
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

        // soft delete
        content.IsDeleted = true;
        _dbc.Contents.Update(content);
        if (full == true && await _dbc.Contents.FindAsync(content.Id) != null) _dbc.Contents.Remove(content);
        await _dbc.SaveChangesAsync();
        return totalSize;
    }

    [Authorize]
    public override async Task<GetSpecialContentIdResponse> GetSpecialContentId(GetSpecialContentIdRequest request,
        ServerCallContext context)
    {
        var storage = await _dbc.Storages
            .Include(v => v.User)
            .SingleOrDefaultAsync(v => v.User.Email == context.GetHttpContext().User.FindFirstValue(ClaimTypes.Email)!);
        if (storage == null)
            throw new RpcException(new Status(StatusCode.NotFound, "User storage not found!"));

        Content? content = null;
        var contents = _dbc.Contents
            .Include(v => v.Storage)
            .Where(v => v.Storage.Id == storage.Id);
        if (request.SpecialContentEnum == GetSpecialContentIdEnum.Home)
            content = contents.FirstOrDefault(v => v.Path == "home");
        if (request.SpecialContentEnum == GetSpecialContentIdEnum.Trashcan)
            content = contents.FirstOrDefault(v => v.Path == "trashcan");

        if (content != null)
            return await Task.FromResult(new GetSpecialContentIdResponse() { ContentId = content.Id });
        throw new RpcException(new Status(StatusCode.NotFound, "Not found!"));
    }

    [Authorize]
    public override async Task<DeletedContentsMessage> GetDeletedContents(EmptyGetContentsMessage request,
        ServerCallContext context)
    {
        var storage = await _dbc.Storages
            .Include(v => v.User)
            .SingleOrDefaultAsync(v => v.User.Email == context.GetHttpContext().User.FindFirstValue(ClaimTypes.Email)!);
        if (storage == null)
            throw new RpcException(new Status(StatusCode.NotFound, "Storage not found!"));
        var contents = await _dbc.Contents
            .Include(v => v.Parent)
            .Include(v => v.Children)
            .Include(v => v.Storage)
            .Where(v => v.Storage.Id == storage.Id)
            .Where(v => v.IsDeleted == true)
            .ToListAsync();
        return await Task.FromResult(new DeletedContentsMessage()
        {
            ContentMessages =
            {
                contents.Select(v => new ContentMessage()
                {
                    Id = v.Id,
                    ContentType = v.ContentType == ContentType.File ? ContentTypeEnum.File : ContentTypeEnum.Folder,
                    Path = v.Path,
                    Name = v.Name,
                    Size = v.Size,
                    Storage = new StorageMessage() { Id = v.Storage.Id },
                    Parent = v.Parent != null ? new ContentMessage() { Id = v.Parent.Id } : null,
                })
            }
        });
    }

    [Authorize]
    public override async Task<ContentsEmpty> CleanTrashCan(ContentsEmpty request, ServerCallContext context)
    {
        var storage = await _dbc.Storages
            .Include(v => v.User)
            .SingleOrDefaultAsync(v => v.User.Email == context.GetHttpContext().User.FindFirstValue(ClaimTypes.Email)!);
        if (storage == null)
            throw new RpcException(new Status(StatusCode.NotFound, "Storage not found!"));
        _dbc.Contents.RemoveRange(_dbc.Contents.Include(v => v.Storage)
            .Where(v => v.IsDeleted == true).Where(v => v.Storage.Id == storage.Id));
        await _dbc.SaveChangesAsync();
        return await Task.FromResult(new ContentsEmpty());
    }

    [Authorize]
    public override async Task<ContentsEmpty> RecoveryContent(RecoveryContentId request, ServerCallContext context)
    {
        var content = await _dbc.Contents
            .Include(v => v.Storage)
            .SingleOrDefaultAsync(v => v.Id == request.ContentId);
        var storage = await _dbc.Storages
            .Include(v => v.User)
            .SingleOrDefaultAsync(v => v.Id == content.Storage.Id);
        if (storage == null || storage.User.Email != context.GetHttpContext().User.FindFirstValue(ClaimTypes.Email)!)
            throw new RpcException(new Status(StatusCode.PermissionDenied, "No access to this storage!"));

        await RecoveryContentRecursion(request.ContentId);
        return await Task.FromResult(new ContentsEmpty());
    }

    private async Task RecoveryContentRecursion(int contentId)
    {
        var content = await _dbc.Contents
            .Include(v => v.Parent)
            .SingleOrDefaultAsync(v => v.Id == contentId);
        if (content == null)
            throw new RpcException(new Status(StatusCode.NotFound, "Content not found!"));
        if (await _dbc.Contents.Where(v => v.Path == content.Path).CountAsync(v => v.Name == content.Name) > 1)
            throw new RpcException(new Status(StatusCode.Cancelled, "The content you are trying to restore has the same name as other content in the same directory!"));
        content!.IsDeleted = false;
        _dbc.Contents.Update(content);
        await _dbc.SaveChangesAsync();
        if (content.Parent != null && content.Parent.ContentType == ContentType.Folder)
            await RecoveryContentRecursion(content.Parent.Id);
    }
}