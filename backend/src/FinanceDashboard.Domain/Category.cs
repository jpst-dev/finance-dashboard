namespace FinanceDashboard.Domain;

public class Category : IUserOwned
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public string Name { get; set; } = "";
    public CategoryKind Kind { get; set; }
    public bool IsDefault { get; set; }
}
