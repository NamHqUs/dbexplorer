using DbExplorer.Application.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DbExplorer.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<DbConnection> DbConnections => Set<DbConnection>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();
    public DbSet<SavedQuery> SavedQueries => Set<SavedQuery>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<SavedQuery>(e =>
        {
            e.HasKey(q => q.Id);
            e.Property(q => q.OwnerUserId).HasMaxLength(450).IsRequired();
            e.Property(q => q.Name).HasMaxLength(200).IsRequired();
            e.Property(q => q.Folder).HasMaxLength(100);
            e.Property(q => q.SqlText).HasMaxLength(100_000).IsRequired();
            e.HasIndex(q => new { q.OwnerUserId, q.ConnectionId });
        });

        builder.Entity<AuditLogEntry>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.UserId).HasMaxLength(450).IsRequired();
            e.Property(a => a.Action).HasMaxLength(100).IsRequired();
            e.Property(a => a.Target).HasMaxLength(400);
            e.Property(a => a.Summary).HasMaxLength(400);
            e.HasIndex(a => new { a.UserId, a.CreatedAt });
        });

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
