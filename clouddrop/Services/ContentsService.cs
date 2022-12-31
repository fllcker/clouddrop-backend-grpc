using System.Security.Claims;
using System.Text.RegularExpressions;
using AutoMapper;
using clouddrop.Data;
using clouddrop.Models;
using clouddrop.Services.Other;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace clouddrop.Services;

public class ContentsService : clouddrop.ContentsService.ContentsServiceBase
{
    private readonly DBC _dbc;
    private readonly IMapService _mapService;

    public ContentsService(DBC dbc, IMapService mapService)
    {
        _dbc = dbc;
        _mapService = mapService;
    }

    private async Task<Storage> GetUserStorage(string? accessEmail = null, int? storageId = null)
    {
        if (accessEmail == null && storageId == null) throw new Exception("accessEmail is null and storageId is null!");
        
        var storages = _dbc.Storages
            .Include(v => v.User);

        var storage = accessEmail != null
            ? await storages.SingleOrDefaultAsync(v => v.User.Email == accessEmail)
            : await storages.SingleOrDefaultAsync(v => v.Id == storageId);
        if (storage == null)
            throw new RpcException(new Status(StatusCode.NotFound, "User storage not found!"));
        return storage;
    }
    
    [Authorize]
    public override async Task<ContentsResponse> GetChildrenContents(GetChildrenContentsRequest request,
        ServerCallContext context)
    {
        var content = await _dbc.Contents
            .Include(v => v.Children)
            .Include(v => v.Parent)
            .Include(v => v.Storage)
            .FirstOrDefaultAsync(v => v.Id == request.ContentId);
        if (content == null)
            throw new RpcException(new Status(StatusCode.NotFound, "Parent content not found!"));

        var storage = await GetUserStorage(storageId: content.Storage.Id);
        if (storage?.User.Email != context.GetHttpContext().User.FindFirstValue(ClaimTypes.Email)!)
            throw new RpcException(new Status(StatusCode.PermissionDenied, "You dont have access to this storage!")); 

        var children = content.Children
            .Where(v => v.IsDeleted == false)
            .Select(v => _mapService.Map<Content, ContentMessage>(v));

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
    public override async Task<ContentMessage> NewFolder(NewFolderMessage request, ServerCallContext context)
    {
        Content? parent = null!;
        Storage? storage = null!;
        parent = await _dbc.Contents
            .Include(v => v.Storage)
            .Include(v => v.Storage.User)
            .FirstOrDefaultAsync(v => v.Id == request.ParentId);

        if (parent == null)
            storage = await GetUserStorage(storageId: request.StorageId);
        else
            storage = parent.Storage;

        if (storage == null)
            throw new RpcException(new Status(StatusCode.NotFound, "Storage not found!"));
        if (storage.User.Email != context.GetHttpContext().User.FindFirstValue(ClaimTypes.Email)!)
            throw new RpcException(new Status(StatusCode.PermissionDenied, "No access to this storage!"));
        
        string pattern = @"[\\/:*?""<>|]";
        Regex regex = new Regex(pattern);
        if (regex.IsMatch(request.Name))
            throw new RpcException(new Status(StatusCode.Aborted, "You use forbidden characters in the name"));

        
        var totalPath = parent != null ? Path.Combine(parent.Path!, request.Name) : Path.Combine("home", request.Name);

        // check if file or folder with this name and path already exists
        if (await _dbc.Contents
                .Where(v => v.IsDeleted == false)
                .Where(v => v.Path == totalPath)
                .CountAsync(v => v.Name == request.Name) != 0)
            throw new RpcException(new Status(StatusCode.AlreadyExists,
                "File or folder with this name and path already exists"));

        var newContent = new Content()
        {
            ContentType = ContentType.Folder,
            ContentState = ContentState.Ready,
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
        
        if (content.ContentState != ContentState.Ready)
            throw new RpcException(new Status(StatusCode.Aborted, "You cannot delete a file until its state is ready!"));

        var storage = await GetUserStorage(storageId: content.Storage.Id);
        if (storage?.User.Email != context.GetHttpContext().User.FindFirstValue(ClaimTypes.Email)!)
            throw new RpcException(new Status(StatusCode.PermissionDenied, "No access to this storage"));

        string contentPath = Path.Combine(Directory.GetCurrentDirectory(),
            "UsersStorage", $"storage{storage.Id}", content.Path ?? "unknown");
        
        if (content.ContentType == ContentType.File && request.Full == true)
        {
            if (File.Exists(contentPath)) File.Delete(contentPath);
            if (content.Size != null) storage.StorageUsed -= (long)content.Size;
        }
        else if (content.ContentType == ContentType.Folder)
        {
            var removedSize = await DeleteContentRecursion(content, full: request.Full);
            if (request.Full == true && Directory.Exists(contentPath)) Directory.Delete(contentPath, recursive: true);
            if (request.Full == true && removedSize != null) storage.StorageUsed -= (long)removedSize;
        }

        // soft delete
        if (await _dbc.Contents.FindAsync(content.Id) != null)
        {
            if (request.Full == true) 
                _dbc.Contents.Remove(content);
            else
            {
                content.IsDeleted = true;
                _dbc.Contents.Update(content);
            }
            await _dbc.SaveChangesAsync();
        }

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
        
        // soft delete
        if (full == true && await _dbc.Contents.FindAsync(content.Id) != null)
        {
            if (content.Size != null) totalSize += content.Size;
            _dbc.Contents.Remove(content);
        }
        else if (await _dbc.Contents.FindAsync(content.Id) != null)
        {
            content.IsDeleted = true;
            _dbc.Contents.Update(content);
        }
        await _dbc.SaveChangesAsync();
        
        foreach (var child in children) 
            totalSize += await DeleteContentRecursion(child, totalSize, full: full);
        return totalSize;
    }

    [Authorize]
    public override async Task<GetSpecialContentIdResponse> GetSpecialContentId(GetSpecialContentIdRequest request,
        ServerCallContext context)
    {
        var storage = await GetUserStorage(context.GetHttpContext().User.FindFirstValue(ClaimTypes.Email)!);

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
        var storage = await GetUserStorage(context.GetHttpContext().User.FindFirstValue(ClaimTypes.Email)!);
        
        var contents = await _dbc.Contents
            .Include(v => v.Parent)
            .Include(v => v.Children)
            .Include(v => v.Storage)
            .Where(v => v.Storage.Id == storage.Id)
            .Where(v => v.IsDeleted == true)
            .ToListAsync();
        return await Task.FromResult(new DeletedContentsMessage()
        {
            ContentMessages = { contents.Select(v => _mapService.Map<Content, ContentMessage>(v)) }
        });
    }

    [Authorize]
    public override async Task<ContentsEmpty> CleanTrashCan(ContentsEmpty request, ServerCallContext context)
    {
        var storage = await GetUserStorage(context.GetHttpContext().User.FindFirstValue(ClaimTypes.Email)!);

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
        if (content?.IsDeleted == false)
            throw new RpcException(new Status(StatusCode.Unknown, "Content is not deleted!"));
        
        var storage = await GetUserStorage(storageId: content.Storage.Id);
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

        var countRepeats = await _dbc.Contents
            .Where(v => v.IsDeleted == false)
            .Where(v => v.Path == content.Path)
            .CountAsync(v => v.Name == content.Name);
        if (countRepeats != 0)
            throw new RpcException(new Status(StatusCode.Cancelled, $"The content you are trying to restore has the same name as other content in the same directory! ({countRepeats})"));
        
        content!.IsDeleted = false;
        _dbc.Contents.Update(content);
        await _dbc.SaveChangesAsync();
        
        if (content.Parent != null && content.Parent.IsDeleted == true && content.Parent.ContentType == ContentType.Folder)
            await RecoveryContentRecursion(content.Parent.Id);
    }

    [Authorize]
    public override async Task<ResultMessage> RenameContent(RenameContentRequest request, ServerCallContext context)
    {
        var content = await _dbc.Contents
            .Include(v => v.Storage.User)
            .SingleOrDefaultAsync(v => v.Id == request.ContentId);
        if (content == null)
            throw new RpcException(new Status(StatusCode.NotFound, "Content not found!"));
        
        if (content.Storage.User.Email != context.GetHttpContext().User.FindFirstValue(ClaimTypes.Email)!)
            throw new RpcException(new Status(StatusCode.PermissionDenied, "No access to this content!"));

        var oldPathArray = content.Path.Split("\\").SkipLast(1);
        var oldPath = String.Join('\\', oldPathArray);
        var newPath = Path.Combine(oldPath, request.NewName);

        if (await _dbc.Contents.CountAsync(v => v.Path == newPath) != 0)
            throw new RpcException(new Status(StatusCode.Unknown, "There is already a file/folder with the same name in this directory!"));

        // moving file in file system
        var oldRealPath = Path.Combine(Directory.GetCurrentDirectory(), 
            "UsersStorage", $"storage{content.Storage.Id}", oldPath, content.Name!);
        var newRealPath = Path.Combine(Directory.GetCurrentDirectory(), 
            "UsersStorage", $"storage{content.Storage.Id}", newPath);

        if (content.ContentType == ContentType.File)
        {
            if (!File.Exists(oldRealPath))
                throw new RpcException(new Status(StatusCode.Aborted, "File is lost!"));
            File.Move(oldRealPath, newRealPath);
        }
        else throw new RpcException(new Status(StatusCode.Aborted, "You cannot change folder name now!"));
        
        content.Path = newPath;
        content.Name = request.NewName;
        await _dbc.SaveChangesAsync();

        return await Task.FromResult(new ResultMessage() { Result = "Ok" });
    }
}