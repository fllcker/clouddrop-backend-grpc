using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;

namespace clouddrop.Models;

public class Content
{
    public int Id { get; set; }
    
    public virtual Storage Storage { get; set; }
    public int? StorageId { get; set; }
    
    public ContentType ContentType { get; set; }
    public string? Path { get; set; }
    public string? Name { get; set; }
    public long? Size { get; set; }
    public bool? IsDeleted { get; set; } = false;
    public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    
    public virtual Content? Parent { get; set; }
    public int? ParentId { get; set; }
    public List<Content> Children = new List<Content>();
}

public enum ContentType
{
    File,
    Folder
}