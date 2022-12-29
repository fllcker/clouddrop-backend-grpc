using System.ComponentModel.DataAnnotations.Schema;

namespace clouddrop.Models;

public class Subscription
{
    public int Id { get; set; }
    
    [ForeignKey("PlanId")]
    public virtual Plan Plan { get; set; }
    [ForeignKey("UserId")]
    public virtual User User { get; set; }
    
    public long StartedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public long FinishAt { get; set; }
}