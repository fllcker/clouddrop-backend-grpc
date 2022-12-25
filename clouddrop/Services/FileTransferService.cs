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
    
    // grpc
    private Dictionary<int, FileStream> _streams;
    private Dictionary<int, string> _filePaths;

    [Authorize]
    public override async Task<Response> StartReceivingFile(StartRequest request, ServerCallContext context)
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
        
        //var parent = storage.Contents.FirstOrDefault(v => v.Id == request.ParentId);
        // 4 test
        if (parent == null)
            throw new RpcException(new Status(StatusCode.NotFound, "Parent not found!"));
        
        if (parent?.Path == null)
            throw new RpcException(new Status(StatusCode.NotFound, "Parent content PATH not found!"));
        
        Content newContent = new Content()
        {
            Storage = storage,
            ContentType = ContentType.File,
            Name = $"{request.Name}.{request.Type}",
            Path = $"{parent.Path}\\{request.Name}.{request.Type}",
            Parent = parent
        };
        _dbc.Contents.Add(newContent);
        await _dbc.SaveChangesAsync();

        if (!Directory.Exists(parent.Path)) Directory.CreateDirectory(parent.Path);
        
        _filePaths.Add(request.OpId, Path.Combine("./UsersStorage", newContent.Path));
        _streams.Add(request.OpId, new FileStream(_filePaths[request.OpId], FileMode.Create));

        return await Task.FromResult(new Response() {Message = "Ok"});
    }

    public override async Task<Response> ReceiveFileChunk(IAsyncStreamReader<Chunk> chunkStream, ServerCallContext context)
    {
        while (await chunkStream.MoveNext())
        {
            var chunk = chunkStream.Current;
            await _streams[chunk.OpId].WriteAsync(chunk.Data.ToArray());
        }

        return await Task.FromResult(new Response() {Message = "Ok"});
    }

    public override async Task<Response> FinishReceivingFile(FinishRequest request, ServerCallContext context)
    {
        await _streams[request.OpId].DisposeAsync();
        _streams[request.OpId] = null!;
        _filePaths[request.OpId] = null!;
        return await Task.FromResult<Response>(new Response() {Message = "Ok"});
    }
}