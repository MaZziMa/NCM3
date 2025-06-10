using Microsoft.EntityFrameworkCore;

namespace NCM3.Models
{
    public class NCMDbContext : DbContext
    {
        public NCMDbContext(DbContextOptions<NCMDbContext> options) : base(options)
        {
        }
        
        public DbSet<Router> Routers { get; set; } = null!;
        public DbSet<RouterConfiguration> RouterConfigurations { get; set; } = null!;
        public DbSet<ConfigTemplate> ConfigTemplates { get; set; } = null!;
        public DbSet<ComplianceRule> ComplianceRules { get; set; } = null!;
        public DbSet<ComplianceResult> ComplianceResults { get; set; } = null!;
        
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Cấu hình cascade delete behavior
            modelBuilder.Entity<ComplianceResult>()
                .HasOne(c => c.Router)
                .WithMany()
                .HasForeignKey(c => c.RouterId)
                .OnDelete(DeleteBehavior.NoAction);
                
            modelBuilder.Entity<ComplianceResult>()
                .HasOne(c => c.Configuration)
                .WithMany()
                .HasForeignKey(c => c.ConfigurationId)
                .OnDelete(DeleteBehavior.NoAction);
                
            modelBuilder.Entity<ComplianceResult>()
                .HasOne(c => c.Rule)
                .WithMany()
                .HasForeignKey(c => c.RuleId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
} 