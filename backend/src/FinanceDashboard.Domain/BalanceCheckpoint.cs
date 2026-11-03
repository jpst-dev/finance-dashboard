namespace FinanceDashboard.Domain;

public class BalanceCheckpoint : IUserOwned
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public Guid AccountId { get; set; }
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
    public CheckpointOrigin Origin { get; set; }
    public Guid? BatchId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
