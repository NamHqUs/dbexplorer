using DbExplorer.Application.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DbExplorer.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<DbConnection> DbConnections => Set<DbConnection>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<DbConnection>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.OwnerUserId).HasMaxLength(450).IsRequired();
            e.Property(c => c.Name).HasMaxLength(200).IsRequired();
            e.Property(c => c.Host).HasMaxLength(255).IsRequired();
            e.Property(c => c.Database).HasMaxLength(128);
            e.Property(c => c.UserName).HasMaxLength(128);
            e.Property(c => c.EncryptedPassword).HasMaxLength(2048);
            e.HasIndex(c => new { c.OwnerUserId, c.Name }).IsUnique();
        });
    }
}
