using System.Security.Claims;
using FinanceDashboard.Infrastructure;

namespace FinanceDashboard.Api.Security;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string? UserId => accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
}
