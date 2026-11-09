using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

[DbContext(typeof(GameStorageContext))]
[Migration("20261109000100_InitialGameStorage")]
public sealed class InitialGameStorage : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE profiles (
            player_id varchar(32) PRIMARY KEY,
            battle_coins integer NOT NULL,
            matches_played integer NOT NULL,
            last_daily_reward_utc date NOT NULL,
            owned_cosmetics text[] NOT NULL DEFAULT ARRAY[]::text[]
        );
        CREATE TABLE identity_tokens (
            token_hash varchar(64) PRIMARY KEY,
            player_id varchar(32) NOT NULL
        );
        CREATE INDEX ix_identity_tokens_player_id ON identity_tokens (player_id);
        CREATE TABLE completed_matches (
            player_id varchar(32) NOT NULL REFERENCES profiles(player_id) ON DELETE CASCADE,
            match_id varchar(32) NOT NULL,
            award integer NOT NULL,
            PRIMARY KEY (player_id, match_id)
        );
        CREATE TABLE ad_spin_challenges (
            challenge_id varchar(32) PRIMARY KEY,
            player_id varchar(32) NOT NULL REFERENCES profiles(player_id) ON DELETE CASCADE,
            day date NOT NULL,
            created_utc timestamp with time zone NOT NULL,
            expires_utc timestamp with time zone NOT NULL,
            completed boolean NOT NULL,
            award integer NOT NULL,
            event_id varchar(128) NOT NULL,
            callback_timestamp varchar(12) NOT NULL
        );
        CREATE INDEX ix_ad_spin_challenges_player_day_completed ON ad_spin_challenges (player_id, day, completed);
        CREATE TABLE rewarded_ad_events (
            event_id varchar(128) PRIMARY KEY,
            player_id varchar(32) NOT NULL,
            challenge_id varchar(32) NOT NULL REFERENCES ad_spin_challenges(challenge_id) ON DELETE CASCADE
        );
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP TABLE IF EXISTS rewarded_ad_events;
        DROP TABLE IF EXISTS ad_spin_challenges;
        DROP TABLE IF EXISTS completed_matches;
        DROP TABLE IF EXISTS identity_tokens;
        DROP TABLE IF EXISTS profiles;
        """);
}

[DbContext(typeof(GameStorageContext))]
[Migration("20261109000200_TelemetryCrashStorage")]
public sealed class TelemetryCrashStorageMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE telemetry_events (
            event_id bigserial PRIMARY KEY,
            name varchar(32) NOT NULL,
            player_id varchar(32) NOT NULL,
            occurred_utc timestamp with time zone NOT NULL
        );
        CREATE INDEX ix_telemetry_events_occurred_event ON telemetry_events (occurred_utc DESC, event_id DESC);
        CREATE TABLE crash_reports (
            report_id bigserial PRIMARY KEY,
            player_id varchar(32) NOT NULL,
            fingerprint varchar(64) NOT NULL,
            build_version varchar(64) NOT NULL,
            platform varchar(32) NOT NULL,
            first_seen_utc timestamp with time zone NOT NULL,
            last_seen_utc timestamp with time zone NOT NULL,
            occurrences integer NOT NULL,
            CONSTRAINT uq_crash_reports_player_fingerprint UNIQUE (player_id, fingerprint)
        );
        CREATE INDEX ix_crash_reports_last_seen ON crash_reports (last_seen_utc);
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP TABLE IF EXISTS crash_reports;
        DROP TABLE IF EXISTS telemetry_events;
        """);
}
