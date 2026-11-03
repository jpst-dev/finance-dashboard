using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FinanceDashboard.Infrastructure;

public class AppDbContext(DbContextOptions<AppDbContext> o) : IdentityDbContext<AppUser>(o);
