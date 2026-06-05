using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using HemisAudit.Models;

namespace HemisAudit.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        public DbSet<Client>        Clients        { get; set; }
        public DbSet<ClientUser>    ClientUsers    { get; set; }
        public DbSet<ValidationRun> ValidationRuns { get; set; }
        public DbSet<AuditLog>      AuditLogs      { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // ── Client ────────────────────────────────────────────────────────
            builder.Entity<Client>(e =>
            {
                e.HasIndex(c => new { c.Name, c.FiscalYear }).IsUnique();
                e.HasMany(c => c.ClientUsers)
                 .WithOne(cu => cu.Client)
                 .HasForeignKey(cu => cu.ClientId)
                 .OnDelete(DeleteBehavior.Cascade);
                e.HasMany(c => c.ValidationRuns)
                 .WithOne(vr => vr.Client)
                 .HasForeignKey(vr => vr.ClientId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            // ── ClientUser ────────────────────────────────────────────────────
            builder.Entity<ClientUser>(e =>
            {
                e.HasIndex(cu => new { cu.ClientId, cu.UserId }).IsUnique();
                e.HasOne(cu => cu.User)
                 .WithMany(u => u.ClientUsers)
                 .HasForeignKey(cu => cu.UserId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            // ── ValidationRun ─────────────────────────────────────────────────
            builder.Entity<ValidationRun>(e =>
            {
                e.HasIndex(vr => new { vr.ClientId, vr.RuleNumber, vr.IsCurrent });
                e.HasIndex(vr => new { vr.ClientId, vr.RunAt });
            });

            // ── AuditLog ──────────────────────────────────────────────────────
            builder.Entity<AuditLog>(e =>
            {
                e.HasIndex(a => a.Timestamp);
                e.HasIndex(a => a.UserId);
            });
        }
    }
}
