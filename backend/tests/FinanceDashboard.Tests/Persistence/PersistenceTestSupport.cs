using FinanceDashboard.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FinanceDashboard.Tests.Persistence;

public sealed class FakeCurrentUser(string? userId) : ICurrentUser
{
    public string? UserId { get; } = userId;
}

public static class PersistenceTestSupport
{
    public static AppDbContext Create(string cs, string? userId) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options, new FakeCurrentUser(userId));
}
