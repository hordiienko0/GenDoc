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
        public DbSet<OrgNode> OrgNodes => Set<OrgNode>();
        public DbSet<Intake> Intakes => Set<Intake>();
        public DbSet<Room> Rooms => Set<Room>();
        public DbSet<Template> Templates => Set<Template>();
        public DbSet<TemplateFieldMapping> TemplateFieldMappings => Set<TemplateFieldMapping>();
        public DbSet<GeneratedDocument> GeneratedDocuments => Set<GeneratedDocument>();
        public DbSet<GeneratedDocumentContent> GeneratedDocumentContents => Set<GeneratedDocumentContent>();
        public DbSet<DocumentAttachment> DocumentAttachments => Set<DocumentAttachment>();
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
        public DbSet<StaffEvent> StaffEvents => Set<StaffEvent>();
        public DbSet<GenerationPackageExportTemplate> GenerationPackageExportTemplates => Set<GenerationPackageExportTemplate>();
        public DbSet<GeneratedGroupDocument> GeneratedGroupDocuments => Set<GeneratedGroupDocument>();
        public DbSet<GeneratedGroupDocumentContent> GeneratedGroupDocumentContents => Set<GeneratedGroupDocumentContent>();

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
            modelBuilder.Entity<GeneratedDocument>(b =>
            {
                b.Property(g => g.FileName).HasColumnName("OutputFileName");
                b.HasIndex(g => new { g.RecipientId, g.TemplateId, g.IsCurrent });
                b.HasIndex(g => g.GeneratedAt);
                b.HasIndex(g => g.RunId);
                b.HasOne(g => g.Content)
                    .WithOne(c => c.GeneratedDocument)
                    .HasForeignKey<GeneratedDocumentContent>(c => c.GeneratedDocumentId)
                    .OnDelete(DeleteBehavior.Cascade);
                b.HasMany(g => g.Attachments)
                    .WithOne(a => a.GeneratedDocument)
                    .HasForeignKey(a => a.GeneratedDocumentId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<GeneratedDocumentContent>()
                .HasKey(c => c.GeneratedDocumentId);

            modelBuilder.Entity<ExportTemplate>()
                .HasMany(t => t.ColumnMappings)
                .WithOne(m => m.ExportTemplate)
                .HasForeignKey(m => m.ExportTemplateId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<GeneratedGroupDocument>(b =>
            {
                b.HasIndex(g => new { g.ExportTemplateId, g.IntakeId, g.IsCurrent });
                b.HasIndex(g => g.GeneratedAt);
                b.HasIndex(g => g.RunId);
                b.HasOne(g => g.Content)
                    .WithOne(c => c.GeneratedGroupDocument)
                    .HasForeignKey<GeneratedGroupDocumentContent>(c => c.GeneratedGroupDocumentId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<GeneratedGroupDocumentContent>()
                .HasKey(c => c.GeneratedGroupDocumentId);

            modelBuilder.Entity<GenerationPackageExportTemplate>()
                .HasOne(t => t.GenerationPackage)
                .WithMany(p => p.ExportTemplates)
                .HasForeignKey(t => t.GenerationPackageId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<OrgNode>()
                .HasOne(n => n.Parent)
                .WithMany(n => n.Children)
                .HasForeignKey(n => n.ParentId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<OrgNode>()
                .HasIndex(n => n.Path);

            modelBuilder.Entity<Recipient>()
                .HasOne(r => r.OrgNode)
                .WithMany(n => n.Recipients)
                .HasForeignKey(r => r.OrgNodeId)
                .OnDelete(DeleteBehavior.SetNull);

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
