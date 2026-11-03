namespace FinanceDashboard.Domain;

public class Account : IUserOwned
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public string Name { get; set; } = "";
    public AccountType Type { get; set; }
    public string Institution { get; set; } = "";
    public string? ExternalIdentifier { get; set; }
    public bool IsArchived { get; set; }

    public bool IsLiability => Type == AccountType.CreditCard;
}
