namespace FinanceDashboard.Domain;

public class OwnAlias : IUserOwned
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public string Fragment { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
