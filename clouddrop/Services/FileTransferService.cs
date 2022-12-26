using System.Security.Claims;
using clouddrop.Data;
using clouddrop.Models;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
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
        var accessEmail = context.GetHttpContext().User.FindFirstValue(ClaimTypes.Email)!;
        var storage = await _dbc.Storages
            .Include(v => v.User)
            .FirstOrDefaultAsync(v => v.Id == request.StorageId);
        
        if (storage == null)
            throw new RpcException(new Status(StatusCode.NotFound, "Storage not found!"));
        
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

        Content newContent = new Content()
        {
            Storage = storage,
            ContentType = ContentType.File,
            Name = $"{request.Name}.{request.Type}",
            Path = $"{parent?.Path ?? "home"}\\{request.Name}.{request.Type}",
            Parent = parent
        };
        _dbc.Contents.Add(newContent);
        await _dbc.SaveChangesAsync();
        
        var savePath = Path.Combine(Directory.GetCurrentDirectory(), 
            "UsersStorage", $"storage{storage.Id}", "home", parent?.Path ?? "");
        
        if (!Directory.Exists(savePath)) Directory.CreateDirectory(savePath);

        var filePath = Path.Combine(savePath, $"{request.Name}.{request.Type}");
        return await Task.FromResult(new StartReceivingResponse() {FilePath = filePath, ContentId = newContent.Id});
    }

    public override async Task<Response> ReceiveFileChunk(IAsyncStreamReader<Chunk> chunkStream, ServerCallContext context)
    {
        long totalSize = 0;
        int? contentId = null;
        string? filePath = null;
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
            await _dbc.SaveChangesAsync();
        }
        
        return await Task.FromResult(new Response() {Message = "Ok"});
    }
}