namespace clouddrop.Models;

public class Storage
{
    public int Id { get; set; }

    public long StorageUsed { get; set; } = 0;
    public long StorageQuote { get; set; } = 52428800; // 50mb
    
    public virtual User User { get; set; }
    public int? UserId { get; set; }

    public List<Content> Contents = new List<Content>();
}