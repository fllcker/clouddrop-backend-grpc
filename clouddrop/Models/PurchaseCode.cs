namespace clouddrop.Models;

public class PurchaseCode
{
    public int Id { get; set; }
    public int SecretNumber { get; set; }
    public int PlanId { get; set; }

    public int Activations { get; set; } = 0;
    public int MaxActivations { get; set; } = 1;
}