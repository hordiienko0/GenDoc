using GenDoc.Data;
using GenDoc.Models;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services;

public class DatabaseSchemaInitializer : IDatabaseSchemaInitializer
{
    private const int CurrentSchemaVersion = 1;

    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public DatabaseSchemaInitializer(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public void EnsureInitialized()
    {
        using var db = _dbFactory.CreateDbContext();
        db.Database.EnsureCreated();

        if (!db.SchemaVersions.Any())
        {
            db.SchemaVersions.Add(new SchemaVersion
            {
                Version = CurrentSchemaVersion,
                AppliedAt = DateTime.Now,
                Description = "Початкова схема"
            });
        }

        if (!db.AppSettings.Any())
        {
            db.AppSettings.Add(new Models.AppSettings());
        }

        if (!db.OrganizationSettings.Any())
        {
            db.OrganizationSettings.Add(new OrganizationSettings());
        }

        db.SaveChanges();
    }
}