namespace FinanceDashboard.Domain;

public enum LinkKind { InvoicePayment, OwnTransfer }
public enum LinkState { Suggested, Confirmed, Rejected }
public enum LinkOrigin { Auto, User }

public class Link : IUserOwned
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public LinkKind Kind { get; set; }
    public LinkState State { get; set; }
    public Guid OutTransactionId { get; set; }
    public Guid InTransactionId { get; set; }
    public LinkOrigin Origin { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
}
