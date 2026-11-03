namespace FinanceDashboard.Domain;

public class Transaction : IUserOwned
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public Guid AccountId { get; set; }
    public DateOnly Date { get; set; }
    public DateOnly? PurchaseDate { get; set; }
    public DateOnly? StatementDate { get; set; }
    public decimal Amount { get; set; }
    public string OriginalDescription { get; set; } = "";
    public string NormalizedDescription { get; set; } = "";
    public Guid? CategoryId { get; set; }
    public TransactionState State { get; set; }
    public TransactionOrigin Origin { get; set; }
    public Guid? BatchId { get; set; }
    public string? SourceKey { get; set; }
    public int Ordinal { get; set; }
    public string? CounterpartyName { get; set; }
    public string? CounterpartyDocument { get; set; }
    public int? InstallmentNumber { get; set; }
    public int? InstallmentTotal { get; set; }
    public string? CardSuffix { get; set; }
    public CardKind? CardKind { get; set; }
    public bool IsDeleted { get; set; }
    public bool EditedByUser { get; set; }
}
