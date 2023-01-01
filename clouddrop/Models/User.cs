using System.ComponentModel.DataAnnotations.Schema;

namespace clouddrop.Models;

public class User
{
    public int Id { get; set; }
    public string Email { get; set; }
    public string Password { get; set; }
    public string Name { get; set; }
    
    // user-info
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Country { get; set; }
    public string? City { get; set; }

    [ForeignKey("UserId")]
    public Storage Storage { get; set; }
    public virtual Subscription Subscription { get; set; }
}