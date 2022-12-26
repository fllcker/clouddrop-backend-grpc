using System.Security.Claims;
using clouddrop.Data;
using clouddrop.Models;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
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

        var parent = await _dbc.Contents
            .Include(v => v.Storage)
            .Where(v => v.Storage.Id == storage.Id)
            .FirstOrDefaultAsync(v => v.Id == request.ParentId);
        
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
        //Console.WriteLine("executed ReceiveFileChunk >>>");
        FileStream fs = null;
        while (await chunkStream.MoveNext())
        {
            var chunk = chunkStream.Current;
            if (fs == null) fs = new FileStream(chunk.FilePath, FileMode.OpenOrCreate);
            await fs.WriteAsync(chunk.Data.ToArray());
            
        }
        if (fs != null) fs.Close();
        return await Task.FromResult(new Response() {Message = "Ok"});
    }
}