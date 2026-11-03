namespace FinanceDashboard.Domain;

public class ImportBatch : IUserOwned
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public Guid AccountId { get; set; }
    public ImportSource Source { get; set; }
    public string FileHash { get; set; } = "";
    public DateOnly? PeriodStart { get; set; }
    public DateOnly? PeriodEnd { get; set; }
    public ImportBatchState State { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? PreviewJson { get; set; }
    public int NewCount { get; set; }
    public int ExistingCount { get; set; }
    public string? WarningsJson { get; set; }
}
