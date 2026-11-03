namespace FinanceDashboard.Domain;

public class CategoryRule : IUserOwned
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public string Pattern { get; set; } = "";
    public Guid CategoryId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
