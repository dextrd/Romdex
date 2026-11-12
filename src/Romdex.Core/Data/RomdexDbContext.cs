using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Romdex.Core.Data;

public partial class RomdexDbContext(DbContextOptions<RomdexDbContext> options) : DbContext(options)
{
    /// <summary>
    /// SQLite options for every context. A list in a query (<c>ids.Contains(x)</c>) goes as one JSON parameter: by
    /// default EF Core makes a parameter per item, and SQLite refuses more than 32,766, which a big catalogue passes.
    /// </summary>
    public static void Sqlite(SqliteDbContextOptionsBuilder sqlite) =>
        sqlite.UseParameterizedCollectionMode(ParameterTranslationMode.Parameter);

    private static readonly TimeSpan WriteTurn = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Brings SQLite's statistics on the tables up to date where they're missing or have grown stale. Without them it can
    /// pick an index that reads a whole table where another would read a few rows, which on a big catalogue takes seconds
    /// a query. Each index is sampled on its own, so writes wait only a moment at a time.
    /// </summary>
    public async Task OptimiseAsync(CancellationToken ct = default)
    {
        var connection = Database.GetDbConnection();
        await Database.OpenConnectionAsync(ct);
        try
        {
            // Asked what it would analyse, across every table (0x10000), rather than letting it (0x01), which would take
            // every table at once.
            var tables = new List<string>();
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA optimize=0x10003;";
                await using var reader = await command.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                    if (OptimiseTable().Match(reader.GetString(0)) is { Success: true } match)
                        tables.Add(match.Groups[1].Value);
            }
            foreach (var table in tables)
            {
                var indexes = new List<string>();
                await using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = $table;";
                    command.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("$table", table));
                    await using var reader = await command.ExecuteReaderAsync(ct);
                    while (await reader.ReadAsync(ct))
                        indexes.Add(reader.GetString(0));
                }
                foreach (var name in indexes.Count > 0 ? indexes : [table])
                {
                    // A pause between each, so writes waiting their turn get it: back to back, they'd seldom see it free.
                    await Task.Delay(WriteTurn, ct);
                    await using var command = connection.CreateCommand();
                    command.CommandText = $"PRAGMA analysis_limit=400; ANALYZE \"{name.Replace("\"", "\"\"")}\";";
                    await command.ExecuteNonQueryAsync(ct);
                }
            }
        }
        finally
        {
            await Database.CloseConnectionAsync();
        }
        // Each connection reads the statistics when it opens, so the pooled ones are let go to take up the new ones.
        Microsoft.Data.Sqlite.SqliteConnection.ClearPool((Microsoft.Data.Sqlite.SqliteConnection)connection);
    }

    [System.Text.RegularExpressions.GeneratedRegex("^ANALYZE \"main\"\\.\"(.+)\"$")]
    private static partial System.Text.RegularExpressions.Regex OptimiseTable();

    public DbSet<PluginInstance> PluginInstances => Set<PluginInstance>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<BackgroundTask> BackgroundTasks => Set<BackgroundTask>();
    public DbSet<User> Users => Set<User>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<Webhook> Webhooks => Set<Webhook>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();
    public DbSet<CatalogueSystem> Systems => Set<CatalogueSystem>();
    public DbSet<Title> Titles => Set<Title>();
    public DbSet<Dump> Dumps => Set<Dump>();
    public DbSet<DumpFile> DumpFiles => Set<DumpFile>();
    public DbSet<LibraryFile> LibraryFiles => Set<LibraryFile>();
    public DbSet<LibraryContent> LibraryContents => Set<LibraryContent>();
    public DbSet<LibraryMatch> LibraryMatches => Set<LibraryMatch>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PluginInstance>(e =>
        {
            e.HasIndex(p => new { p.Type, p.Priority });
        });

        modelBuilder.Entity<Setting>(e =>
        {
            e.HasKey(s => s.Key);
        });

        modelBuilder.Entity<BackgroundTask>(e =>
        {
            e.HasIndex(t => new { t.Status, t.CreatedAt });
        });

        modelBuilder.Entity<ApiKey>(e =>
        {
            e.HasIndex(k => k.Hash).IsUnique();
            e.HasOne(k => k.User).WithMany().HasForeignKey(k => k.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.NormalizedUsername).IsUnique();
        });

        modelBuilder.Entity<WebhookDelivery>(e =>
        {
            e.HasOne(d => d.Webhook).WithMany().OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(d => new { d.Status, d.NextAttemptAt });
            e.HasIndex(d => new { d.WebhookId, d.CreatedAt });
        });

        modelBuilder.Entity<CatalogueSystem>(e =>
        {
            e.HasIndex(s => s.Name).IsUnique();
            e.HasOne<PluginInstance>().WithMany().HasForeignKey(s => s.PluginInstanceId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<CatalogueSystem>().WithMany().HasForeignKey(s => s.BiosForId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Title>(e =>
        {
            e.HasOne(t => t.System).WithMany(s => s.Titles).HasForeignKey(t => t.SystemId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(t => new { t.SystemId, t.Key }).IsUnique();
        });

        modelBuilder.Entity<Dump>(e =>
        {
            e.HasOne<CatalogueSystem>().WithMany(s => s.Dumps).HasForeignKey(d => d.SystemId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(d => d.Title).WithMany(t => t.Dumps).HasForeignKey(d => d.TitleId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(d => new { d.SystemId, d.Name }).IsUnique();
            e.HasIndex(d => d.TitleId);
        });

        modelBuilder.Entity<DumpFile>(e =>
        {
            e.HasOne(f => f.Dump).WithMany(d => d.Files).HasForeignKey(f => f.DumpId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(f => f.DumpId);
            e.HasIndex(f => f.Sha1);
            e.HasIndex(f => f.Md5);
            e.HasIndex(f => new { f.Crc32, f.Size });
        });

        modelBuilder.Entity<LibraryFile>(e =>
        {
            e.HasIndex(f => f.Path).IsUnique();
        });

        modelBuilder.Entity<LibraryContent>(e =>
        {
            e.HasOne(c => c.File).WithMany(f => f.Contents).HasForeignKey(c => c.LibraryFileId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(c => c.LibraryFileId);
        });

        modelBuilder.Entity<LibraryMatch>(e =>
        {
            e.HasKey(m => new { m.ContentId, m.DumpFileId });
            e.HasOne(m => m.Content).WithMany(c => c.Matches).HasForeignKey(m => m.ContentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(m => m.DumpFile).WithMany().HasForeignKey(m => m.DumpFileId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(m => m.DumpFileId);
        });

        // Store enums as their names so the database stays readable and reordering members is safe.
        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(t => t.GetProperties()))
        {
            var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            // Flags stay numbers, so they can be combined and tested.
            if (type.IsEnum && !type.IsDefined(typeof(FlagsAttribute), false))
                property.SetProviderClrType(typeof(string));
        }
    }
}
