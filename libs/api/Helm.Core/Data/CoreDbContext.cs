using Microsoft.EntityFrameworkCore;

namespace Helm.Core.Data;

public class CoreDbContext(DbContextOptions<CoreDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("core");
        b.Entity<Company>().ToTable("companies");
        b.Entity<User>().ToTable("users").HasIndex(u => u.Email).IsUnique();
    }
}
