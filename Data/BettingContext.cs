using BettingAI.Models;
using Microsoft.EntityFrameworkCore;

namespace BettingAI.Data;

public class BettingContext : DbContext
{
    public BettingContext(DbContextOptions<BettingContext> options) : base(options) { }

    public DbSet<Bet> Bets { get; set; }
    public DbSet<TeamStats> TeamStats { get; set; }
    public DbSet<MatchContext> MatchContexts { get; set; }
    public DbSet<LearningNotebook> LearningNotebook { get; set; }
    public DbSet<BetCombo> BetCombos { get; set; }
    public DbSet<ComboLeg> ComboLegs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Bet>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.MatchId).IsRequired();
            entity.Property(e => e.HomeTeam).IsRequired();
            entity.Property(e => e.AwayTeam).IsRequired();
            entity.Property(e => e.BetType).IsRequired();
            entity.Property(e => e.Stake).HasPrecision(10, 2);
            entity.Property(e => e.Confidence).HasPrecision(3, 2);
            entity.Property(e => e.Winnings).HasPrecision(10, 2);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<BetCombo>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Stake).HasPrecision(10, 2);
            entity.Property(e => e.Confidence).HasPrecision(3, 2);
            entity.Property(e => e.CombinedOdds).HasPrecision(10, 2);
            entity.Property(e => e.Winnings).HasPrecision(10, 2);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasMany(e => e.Legs)
                .WithOne(l => l.BetCombo)
                .HasForeignKey(l => l.BetComboId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ComboLeg>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.MatchId).IsRequired();
            entity.Property(e => e.BetType).IsRequired();
            entity.Property(e => e.Odds).HasPrecision(10, 2);
        });
    }
}
