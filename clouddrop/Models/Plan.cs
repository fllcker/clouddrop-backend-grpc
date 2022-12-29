namespace clouddrop.Models;

public class Plan
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    
    public long Price { get; set; } = 0;
    public long AvailableQuote { get; set; }
    public long AvailableSpeed { get; set; }

    public bool IsAvailable { get; set; } = true;
    public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}