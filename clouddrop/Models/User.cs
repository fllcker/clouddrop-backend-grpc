using System.ComponentModel.DataAnnotations.Schema;

namespace clouddrop.Models;

public class User
{
    public int Id { get; set; }
    public string Email { get; set; }
    public string Password { get; set; }
    public string Name { get; set; }
    
    [ForeignKey("UserId")]
    public Storage Storage { get; set; }
    
    
    public virtual Subscription Subscription { get; set; }
}