using System.Security.Claims;
using System.Text.RegularExpressions;
using clouddrop.Data;
using clouddrop.Models;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace clouddrop.Services;

public class FileTransferService : clouddrop.FileTransferService.FileTransferServiceBase
{
    private readonly DBC _dbc;

    public FileTransferService(DBC dbc)
    {
        _dbc = dbc;
    }
    
    [Authorize]
    public override async Task<StartReceivingResponse> StartReceivingFile(StartRequest request, ServerCallContext context)
    {
        var storage = await _dbc.Storages
            .Include(v => v.User)
            .FirstOrDefaultAsync(v => v.Id == request.StorageId);
        if (storage == null)
            throw new RpcException(new Status(StatusCode.NotFound, "Storage not found!"));

        var accessEmail = context.GetHttpContext().User.FindFirstValue(ClaimTypes.Email)!;
        if (storage.User.Email != accessEmail)
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Storage access for you denied!"));

        var parents = _dbc.Contents
            .Include(v => v.Storage)
            .Where(v => v.Storage.Id == storage.Id);
        Content? parent = null!;

        if (request.ParentId != null)
            parent = await parents.FirstOrDefaultAsync(v => v.Id == request.ParentId);
        else
            parent = await parents.FirstOrDefaultAsync();
        
        string pattern = @"[\\/:*?""<>|]";
        Regex regex = new Regex(pattern);
        if (regex.IsMatch(request.Name))
            throw new RpcException(new Status(StatusCode.Aborted, "You use forbidden characters in the name"));
        
        string newContentName = $"{request.Name}.{request.Type}";
        string newContentPath = $"{parent?.Path ?? "home"}\\{request.Name}.{request.Type}";

        int attempts = 0;
        while (await _dbc.Contents
                   .Where(v => v.IsDeleted == false)
                   .CountAsync(v => v.Path == newContentPath) != 0)
        {
            attempts++;
            newContentPath = $"{parent?.Path ?? "home"}\\{request.Name} ({attempts}).{request.Type}";
            newContentName = $"{request.Name} ({attempts}).{request.Type}";
        }

        Content newContent = new Content()
        {
            Storage = storage,
            ContentType = ContentType.File,
            ContentState = ContentState.Uploading,
            Name = newContentName,
            Path = newContentPath,
            Parent = parent
        };
        _dbc.Contents.Add(newContent);
        await _dbc.SaveChangesAsync();
        
        var savePath = Path.Combine(Directory.GetCurrentDirectory(), 
            "UsersStorage", $"storage{storage.Id}", parent?.Path ?? "home");
        
        if (!Directory.Exists(savePath)) Directory.CreateDirectory(savePath);

        var filePath = Path.Combine(savePath, $"{request.Name}.{request.Type}");
        return await Task.FromResult(new StartReceivingResponse() {FilePath = filePath, ContentId = newContent.Id});
    }

    public override async Task<Response> ReceiveFileChunk(IAsyncStreamReader<Chunk> chunkStream, ServerCallContext context)
    {
        long totalSize = 0;
        int? contentId = null;
        string? filePath = null;
        try
        {
            FileStream fs = null;
            while (await chunkStream.MoveNext())
            {
                var chunk = chunkStream.Current;
                if (fs == null) fs = new FileStream(chunk.FilePath, FileMode.OpenOrCreate);
                var bytes = chunk.Data.ToArray();
                await fs.WriteAsync(bytes);
                totalSize += bytes.Length;
                contentId = chunk.ContentId;
                filePath = chunk.FilePath;
            }

            if (fs != null) fs.Close();
        }
        catch (IOException ex) when (ex.InnerException is ConnectionResetException)
        {
            var exContent = await _dbc.Contents.FirstOrDefaultAsync(v => v.Id == contentId);
            if (exContent != null)
            {
                if (File.Exists(exContent.Path)) File.Delete(exContent.Path);
                _dbc.Remove(exContent);
                await _dbc.SaveChangesAsync();
                throw new RpcException(new Status(StatusCode.Aborted, "Connection error! Upload file again"));
            }
        }
        
        // check available place in user storage
        var content = await _dbc.Contents
            .Include(v => v.Storage)
            .FirstOrDefaultAsync(v => v.Id == contentId);
        if (content == null)
            throw new RpcException(new Status(StatusCode.NotFound, "Content Id is wrong!"));
        var storage = content.Storage;
        if ((storage.StorageUsed + totalSize) > storage.StorageQuote)
        {
            if (File.Exists(filePath)) File.Delete(filePath);
            _dbc.Contents.Remove(content);
            await _dbc.SaveChangesAsync();
            throw new RpcException(new Status(StatusCode.Aborted, "Not enough storage space!"));
        }
        else
        {
            var storage2 = await _dbc.Storages.FirstOrDefaultAsync(v => v.Id == storage.Id);
            if (storage2 != null) storage2.StorageUsed += totalSize;
            content.Size = totalSize;
            await _dbc.SaveChangesAsync();
        }
        
        return await Task.FromResult(new Response() {Message = "Ok"});
    }

    [Authorize]
    public override async Task<Response> FinishReceivingFile(FinishReceivingMessage request, ServerCallContext context)
    {
        var content = await _dbc.Contents
            .Include(v => v.Storage)
            .Include(v => v.Storage.User)
            .SingleOrDefaultAsync(v => v.Id == request.ContentId);
        if (content == null)
            throw new RpcException(new Status(StatusCode.NotFound, "Content not found!"));

        if (content.Storage.User.Email != context.GetHttpContext().User.FindFirstValue(ClaimTypes.Email))
            throw new RpcException(new Status(StatusCode.PermissionDenied, "No access to this content!"));

        content.ContentState = ContentState.Ready;
        _dbc.Contents.Update(content);
        await _dbc.SaveChangesAsync();
        return await Task.FromResult(new Response() {Message = "Ok"});
    }


    [Authorize]
    public override async Task SendFileChunks(SendFileChunksRequest request, IServerStreamWriter<SendFileChunk> responseStream, ServerCallContext context)
    {
        var content = await _dbc.Contents
            .Include(v => v.Storage)
            .Include(v => v.Storage.User)
            .SingleOrDefaultAsync(v => v.Id == request.ContentId);
        if (content?.Storage.User.Email != context.GetHttpContext().User.FindFirstValue(ClaimTypes.Email)!)
            throw new RpcException(new Status(StatusCode.PermissionDenied, "You dont have access to this storage"));
        if (content.ContentType == ContentType.Folder)
            throw new RpcException(new Status(StatusCode.Aborted, "You cannot download the folder"));
        if (content.ContentState == ContentState.Ready || content.ContentState == ContentState.None)
            throw new RpcException(new Status(StatusCode.Aborted, "It is forbidden to download files while their state is ready or none, change the state to downloading!"));
        
        var contentPath = Path.Combine(Directory.GetCurrentDirectory(), 
            "UsersStorage", $"storage{content.Storage.Id}", content.Path ?? "unknown");

        if (!File.Exists(contentPath))
            throw new RpcException(new Status(StatusCode.NotFound, "File not found!"));

        FileInfo fileInfo = new FileInfo(contentPath);
        long fileSize = fileInfo.Length;
        long chunkSize = 1024 * 1024; // 1 MB
        int chunkCount = (int)Math.Ceiling(fileSize / (double)chunkSize);

        byte[] buffer = new byte[chunkSize];
        int bytesRead;

        await using (FileStream stream = new FileStream(contentPath, FileMode.Open))
        {
            while ((bytesRead = stream.Read(buffer, 0, (int)chunkSize)) > 0)
            {
                var chunkBytes = buffer.Take(bytesRead).ToArray();
                var chunk = new SendFileChunk()
                {
                    Data = ByteString.CopyFrom(chunkBytes),
                    FileName = content.Name,
                    TotalSize = fileSize
                };
                await responseStream.WriteAsync(chunk);
            }
        }

        content.ContentState = ContentState.Ready;
        await _dbc.SaveChangesAsync();
    }

    public override async Task<Response> SendFileStateChange(SendFileStateChangeRequest request, ServerCallContext context)
    {
        var content = await _dbc.Contents
            .Include(v => v.Storage)
            .Include(v => v.Storage.User)
            .SingleOrDefaultAsync(v => v.Id == request.ContentId);
        if (content == null)
            throw new RpcException(new Status(StatusCode.NotFound, "Content not found!"));

        if (content.Storage.User.Email != context.GetHttpContext().User.FindFirstValue(ClaimTypes.Email))
            throw new RpcException(new Status(StatusCode.PermissionDenied, "No access to this content!"));

        if (request.State == SendFileStateChangeEnum.Start)
        {
            if (content.ContentState != ContentState.Ready)
                throw new RpcException(new Status(StatusCode.Aborted, "The file is not ready for download!"));
            content.ContentState = ContentState.Downloading;
        }
        else if (request.State == SendFileStateChangeEnum.Finish)
            content.ContentState = ContentState.Ready;
        
        _dbc.Contents.Update(content);
        await _dbc.SaveChangesAsync();
        return await Task.FromResult(new Response() {Message = "Ok"});
    }
}