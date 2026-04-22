using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using VkPostman.Core.Models;

namespace VkPostman.Data;

public class VkPostmanDbContext : DbContext
{
    public VkPostmanDbContext(DbContextOptions<VkPostmanDbContext> options) : base(options)
    {
    }

    public DbSet<PostTemplate>          PostTemplates          { get; set; } = null!;
    public DbSet<TargetGroup>           TargetGroups           { get; set; } = null!;
    public DbSet<PostDraft>             PostDrafts             { get; set; } = null!;
    public DbSet<PlaceholderDefinition> PlaceholderDefinitions { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ---- PlaceholderDefinition (shared library) ----
        modelBuilder.Entity<PlaceholderDefinition>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Key).IsRequired().HasMaxLength(50);
            entity.Property(e => e.DisplayName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(200);
            entity.Property(e => e.DefaultValue).HasMaxLength(200);
            entity.HasIndex(e => e.Key).IsUnique();
        });

        // ---- PostTemplate ----
        modelBuilder.Entity<PostTemplate>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.BodyTemplate).IsRequired();

            entity.Property(e => e.DefaultThemeTags)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>());
        });

        // ---- TargetGroup ----
        modelBuilder.Entity<TargetGroup>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ScreenName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.DisplayName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.Notes).HasMaxLength(1000);

            entity.HasIndex(e => e.ScreenName).IsUnique();
            entity.Ignore(e => e.PublicUrl);

            entity.Property(e => e.MandatoryTags)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>());

            entity.HasOne(g => g.PostTemplate)
                  .WithMany()
                  .HasForeignKey(g => g.PostTemplateId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        // ---- PostDraft ----
        modelBuilder.Entity<PostDraft>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).HasMaxLength(300);
            entity.Property(e => e.CommonText);

            entity.Property(e => e.PlaceholderValues)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, (JsonSerializerOptions?)null) ?? new Dictionary<string, string>());

            entity.Property(e => e.ThemeTags)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>());

            entity.Property(e => e.TargetGroupIds)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<int>>(v, (JsonSerializerOptions?)null) ?? new List<int>());
        });
    }
}
