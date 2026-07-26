using GenDoc.Models;
using GenDoc.Models.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace GenDoc.Data
{
    public class AppDbContext : DbContext
    {
        private readonly IDbPasswordProvider _passwordProvider;

        public AppDbContext(IDbPasswordProvider passwordProvider)
        {
            _passwordProvider = passwordProvider;
            SqlCipherBootstrapper.EnsureInitialized();
        }

        public DbSet<Recipient> Recipients => Set<Recipient>();
        public DbSet<Unit> Units => Set<Unit>();
        public DbSet<Room> Rooms => Set<Room>();
        public DbSet<Template> Templates => Set<Template>();
        public DbSet<TemplateFieldMapping> TemplateFieldMappings => Set<TemplateFieldMapping>();
        public DbSet<GeneratedDocument> GeneratedDocuments => Set<GeneratedDocument>();
        public DbSet<GenerationPackage> GenerationPackages => Set<GenerationPackage>();
        public DbSet<GenerationPackageTemplate> GenerationPackageTemplates => Set<GenerationPackageTemplate>();
        public DbSet<GenerationPackageRun> GenerationPackageRuns => Set<GenerationPackageRun>();
        public DbSet<UserProfile> Users => Set<UserProfile>();
        public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();
        public DbSet<Models.AppSettings> AppSettings => Set<Models.AppSettings>();
        public DbSet<OrganizationSettings> OrganizationSettings => Set<OrganizationSettings>();
        public DbSet<SchemaVersion> SchemaVersions => Set<SchemaVersion>();
        public DbSet<ExportTemplate> ExportTemplates => Set<ExportTemplate>();
        public DbSet<ExportTemplateColumnMapping> ExportTemplateColumnMappings => Set<ExportTemplateColumnMapping>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (optionsBuilder.IsConfigured) return;

            var password = _passwordProvider.Password
                ?? throw new InvalidOperationException(
                    "Пароль бази ще не підтверджено. Спочатку виконайте вхід через IDatabaseUnlockService.");

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = DbPaths.DatabasePath,
                Password = password
            }.ToString();

            optionsBuilder.UseSqlite(connectionString);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<GeneratedDocument>()
                .HasIndex(g => new { g.RecipientId, g.TemplateId })
                .IsUnique();

            modelBuilder.Entity<ExportTemplate>()
                .HasMany(t => t.ColumnMappings)
                .WithOne(m => m.ExportTemplate)
                .HasForeignKey(m => m.ExportTemplateId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Room>()
                .HasIndex(r => new { r.Building, r.Number })
                .IsUnique()
                .HasFilter("\"DeletedAt\" IS NULL");

            ApplySoftDeleteFilters(modelBuilder);
        }

        private static void ApplySoftDeleteFilters(ModelBuilder modelBuilder)
        {
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                if (!typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType)) continue;

                var method = typeof(AppDbContext)
                    .GetMethod(nameof(SetSoftDeleteFilter), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(entityType.ClrType);

                method.Invoke(null, new object[] { modelBuilder });
            }
        }

        private static void SetSoftDeleteFilter<TEntity>(ModelBuilder modelBuilder)
            where TEntity : class, ISoftDeletable
        {
            modelBuilder.Entity<TEntity>().HasQueryFilter(e => e.DeletedAt == null);
        }
    }
}
