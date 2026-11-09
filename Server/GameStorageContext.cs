using Microsoft.EntityFrameworkCore;

public sealed class GameStorageContext(DbContextOptions<GameStorageContext> options) : DbContext(options)
{
    public DbSet<ProfileRow> Profiles => Set<ProfileRow>();
    public DbSet<IdentityTokenRow> IdentityTokens => Set<IdentityTokenRow>();
    public DbSet<CompletedMatchRow> CompletedMatches => Set<CompletedMatchRow>();
    public DbSet<AdSpinChallengeRow> AdSpinChallenges => Set<AdSpinChallengeRow>();
    public DbSet<RewardedAdEventRow> RewardedAdEvents => Set<RewardedAdEventRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProfileRow>(entity =>
        {
            entity.ToTable("profiles");
            entity.HasKey(row => row.PlayerId);
            entity.Property(row => row.PlayerId).HasColumnName("player_id").HasMaxLength(32);
            entity.Property(row => row.BattleCoins).HasColumnName("battle_coins");
            entity.Property(row => row.MatchesPlayed).HasColumnName("matches_played");
            entity.Property(row => row.LastDailyRewardUtc).HasColumnName("last_daily_reward_utc");
            entity.Property(row => row.OwnedCosmetics).HasColumnName("owned_cosmetics").HasColumnType("text[]");
        });
        modelBuilder.Entity<IdentityTokenRow>(entity =>
        {
            entity.ToTable("identity_tokens");
            entity.HasKey(row => row.TokenHash);
            entity.Property(row => row.TokenHash).HasColumnName("token_hash").HasMaxLength(64);
            entity.Property(row => row.PlayerId).HasColumnName("player_id").HasMaxLength(32);
            entity.HasIndex(row => row.PlayerId);
        });
        modelBuilder.Entity<CompletedMatchRow>(entity =>
        {
            entity.ToTable("completed_matches");
            entity.HasKey(row => new { row.PlayerId, row.MatchId });
            entity.Property(row => row.PlayerId).HasColumnName("player_id").HasMaxLength(32);
            entity.Property(row => row.MatchId).HasColumnName("match_id").HasMaxLength(32);
            entity.Property(row => row.Award).HasColumnName("award");
            entity.HasOne<ProfileRow>().WithMany().HasForeignKey(row => row.PlayerId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<AdSpinChallengeRow>(entity =>
        {
            entity.ToTable("ad_spin_challenges");
            entity.HasKey(row => row.ChallengeId);
            entity.Property(row => row.ChallengeId).HasColumnName("challenge_id").HasMaxLength(32);
            entity.Property(row => row.PlayerId).HasColumnName("player_id").HasMaxLength(32);
            entity.Property(row => row.Day).HasColumnName("day");
            entity.Property(row => row.CreatedUtc).HasColumnName("created_utc");
            entity.Property(row => row.ExpiresUtc).HasColumnName("expires_utc");
            entity.Property(row => row.Completed).HasColumnName("completed");
            entity.Property(row => row.Award).HasColumnName("award");
            entity.Property(row => row.EventId).HasColumnName("event_id");
            entity.Property(row => row.CallbackTimestamp).HasColumnName("callback_timestamp");
            entity.HasIndex(row => new { row.PlayerId, row.Day, row.Completed });
            entity.HasOne<ProfileRow>().WithMany().HasForeignKey(row => row.PlayerId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<RewardedAdEventRow>(entity =>
        {
            entity.ToTable("rewarded_ad_events");
            entity.HasKey(row => row.EventId);
            entity.Property(row => row.EventId).HasColumnName("event_id").HasMaxLength(128);
            entity.Property(row => row.PlayerId).HasColumnName("player_id").HasMaxLength(32);
            entity.Property(row => row.ChallengeId).HasColumnName("challenge_id").HasMaxLength(32);
            entity.HasOne<AdSpinChallengeRow>().WithMany().HasForeignKey(row => row.ChallengeId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

public sealed class ProfileRow
{
    public string PlayerId { get; set; } = string.Empty;
    public int BattleCoins { get; set; }
    public int MatchesPlayed { get; set; }
    public DateOnly LastDailyRewardUtc { get; set; }
    public string[] OwnedCosmetics { get; set; } = Array.Empty<string>();
}

public sealed class IdentityTokenRow
{
    public string TokenHash { get; set; } = string.Empty;
    public string PlayerId { get; set; } = string.Empty;
}

public sealed class CompletedMatchRow
{
    public string PlayerId { get; set; } = string.Empty;
    public string MatchId { get; set; } = string.Empty;
    public int Award { get; set; }
}

public sealed class AdSpinChallengeRow
{
    public string ChallengeId { get; set; } = string.Empty;
    public string PlayerId { get; set; } = string.Empty;
    public DateOnly Day { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset ExpiresUtc { get; set; }
    public bool Completed { get; set; }
    public int Award { get; set; }
    public string EventId { get; set; } = string.Empty;
    public string CallbackTimestamp { get; set; } = string.Empty;
}

public sealed class RewardedAdEventRow
{
    public string EventId { get; set; } = string.Empty;
    public string PlayerId { get; set; } = string.Empty;
    public string ChallengeId { get; set; } = string.Empty;
}
