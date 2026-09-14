using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Content.Server._Solreign.SeasonLedger;

/// <summary>
///     Self-contained SQLite persistence for the Season Ledger. This is deliberately independent of the
///     core <c>IServerDbManager</c> (EF Core / migration-bound) — it owns its own connection string and
///     schema so the fork stays fully additive. Safe to unit-test against a temp DB file.
///
///     Schema:
///       player_stats(user_id TEXT, season_id TEXT, tours INT, captain_clean INT, antag_wins INT,
///                    early_deaths INT, standing_total INT, contracts_completed INT, contract_score INT,
///                    hr_points INT, PRIMARY KEY(user_id, season_id))
///       round_log(round_id INT, season_id TEXT, user_id TEXT, ended_utc TEXT, gamemode TEXT,
///                 payload_json TEXT, payload_hash TEXT)
///       contract_log(round_id INT, season_id TEXT, user_id TEXT, contract_id TEXT, scope TEXT,
///                    occurrence INT, completed_utc TEXT)
///                                                  -- private per-completion audit feed
///       round_envelopes(...)                       -- global exactly-once round marker
///       ledger_outbox(...)                         -- private-internal aggregate events; not a public export
///       meta(key TEXT PRIMARY KEY, value TEXT)   -- holds the current season id
///       title_grants(user_id TEXT, season_id TEXT, title TEXT, PRIMARY KEY(user_id, season_id))
///                                                 -- last title ceremonially ANNOUNCED per account/season
///       admin_title_grants(user_id TEXT, season_id TEXT, title TEXT, granted_by TEXT, granted_utc TEXT,
///                          PRIMARY KEY(user_id, season_id))
///                                                 -- admin-granted display title per account/season
///                                                 -- (community rewards program; see SeasonLedgerStore.AdminTitles.cs)
///       first_death(user_id TEXT PRIMARY KEY, ...) -- once-per-account-EVER authored-first-death claim,
///                                                 -- deliberately NOT season-scoped (a season bump does not
///                                                 -- resurrect anyone; see SeasonLedgerStore.FirstDeath.cs)
///       social_firsts(user_id TEXT, flag_id TEXT, PRIMARY KEY(user_id, flag_id))
///                                                 -- once-per-account-EVER social-milestone claims (first
///                                                 -- chirp answered, first heal by another, ...), same
///                                                 -- career scoping as first_death; see
///                                                 -- SeasonLedgerStore.SocialFirsts.cs
///       mark(user_id TEXT PRIMARY KEY, ...)       -- once-per-account-EVER "the Mark" keepsake claim
///                                                 -- (Continuity Garden), deliberately NOT season-scoped
///                                                 -- (a season bump does not uproot anyone; see
///                                                 -- SeasonLedgerStore.Mark.cs)
///       station_audit_log(round_id INTEGER PRIMARY KEY, ...) -- one compact summary row per shift's
///                                                 -- Station Audit (v14 wave-1 #3), round-scoped (not
///                                                 -- account-scoped) — the station remembering its own
///                                                 -- shifts, not any one crew member's career; see
///                                                 -- SeasonLedgerStore.StationAudits.cs
///       noticeboard_notes(id INTEGER PRIMARY KEY, board_id TEXT, user_id TEXT, is_providence INT,
///                         author_display TEXT, body TEXT, posted_utc TEXT, expires_utc TEXT,
///                         hidden INT, hidden_utc TEXT, hidden_reason TEXT)
///                                                 -- crew Noticeboards (docs/council/2026-07-17-
///                                                 -- player-text-safety.md): classifier-approved
///                                                 -- player/PROVIDENCE notes, flat CVar-tunable
///                                                 -- expiry, deliberately NOT season-scoped (a
///                                                 -- season bump does not clear the board); see
///                                                 -- SeasonLedgerStore.Noticeboards.cs
///       noticeboard_authors(user_id TEXT PRIMARY KEY, last_post_utc TEXT)
///                                                 -- durable per-account cooldown clock, kept
///                                                 -- independent of noticeboard_notes so the
///                                                 -- 24h cooldown survives the expiry sweep
///                                                 -- deleting the note row itself; see
///                                                 -- SeasonLedgerStore.Noticeboards.cs
///       directives_fax_streak(user_id TEXT PRIMARY KEY, current_streak INT, best_streak INT,
///                              last_round_id INT, updated_utc TEXT)
///                                                 -- per-account Directives Fax compliance streak
///                                                 -- (v14 wave-1 item #1), deliberately NOT
///                                                 -- season-scoped, presence-aware (absence never
///                                                 -- resets — see SeasonLedgerStore.DirectivesFax.cs)
///       library_works(id INTEGER PRIMARY KEY, archive_id TEXT, author_guid TEXT, is_providence INT,
///                     author_display TEXT, title TEXT, body TEXT, round_id INT, submitted_utc TEXT,
///                     hidden INT, hidden_utc TEXT, hidden_reason TEXT)
///                                                 -- STATION-LIBRARY (wave-2, docs/council/2026-07-17-
///                                                 -- player-text-safety.md's write-path posture is LAW
///                                                 -- where applicable): classifier-approved player/
///                                                 -- PROVIDENCE works. Deliberately NO expiry column —
///                                                 -- a library persists; see
///                                                 -- SeasonLedgerStore.LibraryWorks.cs
///       contracts_streak(user_id TEXT PRIMARY KEY, current_streak INT, best_streak INT,
///                         last_round_id INT, updated_utc TEXT)
///                                                 -- per-account Solreign Contracts consecutive-shift
///                                                 -- streak (v14 quest-board extension, spec §3),
///                                                 -- structurally cloned from directives_fax_streak;
///                                                 -- deliberately NOT season-scoped; gated end-to-end
///                                                 -- by CCVars.SolreignContractsQuestBoardEnabled
///                                                 -- (dormant rows while off) — see
///                                                 -- SeasonLedgerStore.ContractsStreak.cs
///       pending_contracts_streak_folds(round_id INT, user_id TEXT, completed_this_round INT,
///                         created_utc TEXT, consumed_utc TEXT NULL,
///                         PRIMARY KEY(round_id, user_id))
///                                                 -- durable fold intent journaled inside the
///                                                 -- canonical envelope transaction; recovery
///                                                 -- applies unconsumed rows exactly once
///
///     WAL mode keeps game-side reads and writes cooperative. The v4 outbox is private-internal and is not an
///     approved web/Discord projection; external consumers do not open the authoritative database directly.
/// </summary>
public sealed partial class SeasonLedgerStore
{
    public const string DefaultSeason = "S1";
    private const string CurrentSeasonKey = "current_season";
    internal const int CurrentSchemaVersion = 6;

    private readonly string _connectionString;
    private readonly IRoundEnvelopeFaultInjector _roundEnvelopeFaultInjector;
    private readonly IRoundEnvelopeSpoolFaultInjector _roundEnvelopeSpoolFaultInjector;
    private readonly ILedgerRetryRuntime _ledgerRetryRuntime;
    private readonly SeasonLedgerSpool _spool;

    // Serializes writes/season bumps within this process. SQLite handles cross-process locking; this
    // just keeps our own async callers from racing the read-modify-write upsert.
    private readonly SemaphoreSlim _lock = new(1, 1);

    private bool _initialized;

    public SeasonLedgerStore(string dbPath)
        : this(
            dbPath,
            NoopRoundEnvelopeFaultInjector.Instance,
            ImmediateRetryRuntime.Instance,
            NoopRoundEnvelopeSpoolFaultInjector.Instance)
    {
    }

    internal SeasonLedgerStore(string dbPath, IRoundEnvelopeFaultInjector roundEnvelopeFaultInjector)
        : this(
            dbPath,
            roundEnvelopeFaultInjector,
            ImmediateRetryRuntime.Instance,
            NoopRoundEnvelopeSpoolFaultInjector.Instance)
    {
    }

    internal SeasonLedgerStore(
        string dbPath,
        IRoundEnvelopeFaultInjector roundEnvelopeFaultInjector,
        ILedgerRetryRuntime ledgerRetryRuntime,
        IRoundEnvelopeSpoolFaultInjector roundEnvelopeSpoolFaultInjector)
    {
        if (string.IsNullOrWhiteSpace(dbPath))
            throw new ArgumentException("Database path must be provided.", nameof(dbPath));
        ArgumentNullException.ThrowIfNull(roundEnvelopeFaultInjector);
        ArgumentNullException.ThrowIfNull(ledgerRetryRuntime);
        ArgumentNullException.ThrowIfNull(roundEnvelopeSpoolFaultInjector);

        // Ensure the native SQLite provider is initialized (Microsoft.Data.Sqlite.Core needs a bundle).
        SQLitePCL.Batteries_V2.Init();

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            // Private page caches avoid process-local shared-cache table locks; WAL still provides
            // cooperative readers while SQLite's file locks serialize writers across instances.
            Cache = SqliteCacheMode.Private,
            // Ordinary Ledger operations retain a bounded one-second provider wait. The round-end
            // path scopes its fail-fast native timeout and deterministic retry policy in OpenAsync.
            DefaultTimeout = 1,
        }.ToString();
        _roundEnvelopeFaultInjector = roundEnvelopeFaultInjector;
        _ledgerRetryRuntime = ledgerRetryRuntime;
        _roundEnvelopeSpoolFaultInjector = roundEnvelopeSpoolFaultInjector;
        _spool = new SeasonLedgerSpool(dbPath);
    }

    private async Task<SqliteConnection> OpenAsync(bool requireWriterAvailable = false)
    {
        var conn = new SqliteConnection(_connectionString);
        try
        {
            await conn.OpenAsync();

            if (!_initialized)
                await EnsureSchemaAsync(conn);

            if (requireWriterAvailable)
            {
                // Schema initialization deliberately retains the provider's bounded safety wait.
                // The raw probe is immediate, while Microsoft.Data.Sqlite retains its minimum
                // finite one-second managed reservation bound. Never set DefaultTimeout to zero:
                // the provider defines zero as an unbounded wait. A race after the probe can cost
                // at most one second before control returns to the outer four-attempt policy. The
                // adversarial four-race ceiling is therefore 4 seconds plus 175 ms outer delay.
                VerifyWriterAvailable(conn);
            }

            return conn;
        }
        catch
        {
            await conn.DisposeAsync();
            throw;
        }
    }

    private async Task EnsureSchemaAsync(SqliteConnection conn)
    {
        // Fast fail before journal_mode can mutate a database already owned by a newer binary. This first
        // read is advisory; the authoritative check is repeated under the write reservation below.
        await EnsureSupportedSchemaVersionAsync(conn);

        await using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL;";
            await pragma.ExecuteNonQueryAsync();
        }

        // Acquire the write reservation before reading user_version. Otherwise a newer process can migrate
        // after our check but before this transaction and this binary can overwrite its version marker.
        await using var tx = conn.BeginTransaction(deferred: false);

        await EnsureSupportedSchemaVersionAsync(conn, tx);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS player_stats (
                    user_id      TEXT NOT NULL,
                    season_id    TEXT NOT NULL,
                    tours        INTEGER NOT NULL DEFAULT 0,
                    captain_clean INTEGER NOT NULL DEFAULT 0,
                    antag_wins   INTEGER NOT NULL DEFAULT 0,
                    early_deaths INTEGER NOT NULL DEFAULT 0,
                    standing_total INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY (user_id, season_id)
                );
                CREATE TABLE IF NOT EXISTS round_log (
                    round_id    INTEGER,
                    season_id   TEXT,
                    user_id     TEXT,
                    ended_utc   TEXT NOT NULL,
                    gamemode    TEXT,
                    payload_json TEXT,
                    payload_hash TEXT
                );
                CREATE TABLE IF NOT EXISTS contract_log (
                    round_id     INTEGER,
                    season_id    TEXT NOT NULL,
                    user_id      TEXT NOT NULL,
                    contract_id  TEXT NOT NULL,
                    scope        TEXT NOT NULL,
                    occurrence   INTEGER,
                    completed_utc TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS meta (
                    key   TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS title_grants (
                    user_id   TEXT NOT NULL,
                    season_id TEXT NOT NULL,
                    title     TEXT NOT NULL,
                    PRIMARY KEY (user_id, season_id)
                );
                CREATE TABLE IF NOT EXISTS admin_title_grants (
                    user_id     TEXT NOT NULL,
                    season_id   TEXT NOT NULL,
                    title       TEXT NOT NULL,
                    granted_by  TEXT NOT NULL,
                    granted_utc TEXT NOT NULL,
                    PRIMARY KEY (user_id, season_id)
                );
                CREATE TABLE IF NOT EXISTS wingmate_blocks (
                    blocker_user_id TEXT NOT NULL,
                    blocked_user_id TEXT NOT NULL,
                    created_utc     TEXT NOT NULL,
                    PRIMARY KEY (blocker_user_id, blocked_user_id)
                );
                CREATE TABLE IF NOT EXISTS round_envelopes (
                    round_id INTEGER PRIMARY KEY CHECK(round_id > 0),
                    season_id TEXT NOT NULL,
                    envelope_hash TEXT NOT NULL CHECK(length(envelope_hash) = 64),
                    envelope_schema INTEGER NOT NULL,
                    committed_utc TEXT NOT NULL,
                    gamemode TEXT NOT NULL,
                    player_count INTEGER NOT NULL,
                    contract_count INTEGER NOT NULL
                );
                CREATE TABLE IF NOT EXISTS ledger_outbox (
                    sequence INTEGER PRIMARY KEY AUTOINCREMENT,
                    event_id TEXT NOT NULL UNIQUE,
                    event_type TEXT NOT NULL,
                    schema_version INTEGER NOT NULL,
                    privacy_class TEXT NOT NULL,
                    payload_json TEXT NOT NULL,
                    created_utc TEXT NOT NULL,
                    exported_utc TEXT NULL
                );
                CREATE TABLE IF NOT EXISTS first_death (
                    user_id        TEXT PRIMARY KEY,
                    round_id       INTEGER NOT NULL,
                    character_name TEXT NOT NULL,
                    cause          TEXT NOT NULL,
                    tours_at_death INTEGER NOT NULL,
                    title_at_death TEXT NOT NULL,
                    epitaph_id     TEXT NOT NULL,
                    died_at_utc    TEXT NOT NULL,
                    rehire_shown   INTEGER NOT NULL DEFAULT 0,
                    crypt_reported INTEGER NOT NULL DEFAULT 0
                );
                CREATE TABLE IF NOT EXISTS social_firsts (
                    user_id     TEXT NOT NULL,
                    flag_id     TEXT NOT NULL,
                    round_id    INTEGER NOT NULL,
                    claimed_utc TEXT NOT NULL,
                    PRIMARY KEY (user_id, flag_id)
                );
                CREATE TABLE IF NOT EXISTS mark (
                    user_id           TEXT PRIMARY KEY,
                    kind              TEXT NOT NULL,
                    planted_round_id  INTEGER NOT NULL,
                    planted_utc       TEXT NOT NULL,
                    planted_map       TEXT NOT NULL,
                    character_name    TEXT NOT NULL,
                    tours_at_planting INTEGER NOT NULL,
                    slot_index        INTEGER NOT NULL,
                    nudge_shown       INTEGER NOT NULL DEFAULT 0,
                    last_visit_utc    TEXT NOT NULL,
                    last_visit_stage  INTEGER NOT NULL DEFAULT 0
                );
                CREATE TABLE IF NOT EXISTS station_audit_log (
                    round_id                  INTEGER PRIMARY KEY,
                    season_id                 TEXT NOT NULL,
                    ended_utc                 TEXT NOT NULL,
                    shift_duration_minutes    INTEGER NOT NULL,
                    crew_count                INTEGER NOT NULL,
                    death_count               INTEGER NOT NULL,
                    first_death_commemorated  INTEGER NOT NULL DEFAULT 0,
                    directive_title           TEXT,
                    directive_outcome_reported INTEGER NOT NULL DEFAULT 0,
                    directive_outcome_fulfilled INTEGER NOT NULL DEFAULT 0,
                    stipends_processed        INTEGER NOT NULL DEFAULT 0,
                    bounty_verdicts           INTEGER NOT NULL DEFAULT 0,
                    notable_event_count       INTEGER NOT NULL DEFAULT 0,
                    commendation_name         TEXT,
                    commendation_score        INTEGER NOT NULL DEFAULT 0,
                    item_of_concern_id        TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS noticeboard_notes (
                    id             INTEGER PRIMARY KEY AUTOINCREMENT,
                    board_id       TEXT NOT NULL,
                    user_id        TEXT NOT NULL,
                    is_providence  INTEGER NOT NULL DEFAULT 0,
                    author_display TEXT NOT NULL,
                    body           TEXT NOT NULL,
                    posted_utc     TEXT NOT NULL,
                    expires_utc    TEXT NOT NULL,
                    hidden         INTEGER NOT NULL DEFAULT 0,
                    hidden_utc     TEXT NULL,
                    hidden_reason  TEXT NULL
                );
                CREATE INDEX IF NOT EXISTS noticeboard_notes_board_active
                    ON noticeboard_notes (board_id, hidden, expires_utc);
                CREATE TABLE IF NOT EXISTS noticeboard_authors (
                    user_id       TEXT PRIMARY KEY,
                    last_post_utc TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS directives_fax_streak (
                    user_id        TEXT PRIMARY KEY,
                    current_streak INTEGER NOT NULL DEFAULT 0,
                    best_streak    INTEGER NOT NULL DEFAULT 0,
                    last_round_id  INTEGER NOT NULL DEFAULT 0,
                    updated_utc    TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS library_works (
                    id             INTEGER PRIMARY KEY AUTOINCREMENT,
                    archive_id     TEXT NOT NULL,
                    author_guid    TEXT NOT NULL,
                    is_providence  INTEGER NOT NULL DEFAULT 0,
                    author_display TEXT NOT NULL,
                    title          TEXT NOT NULL,
                    body           TEXT NOT NULL,
                    round_id       INTEGER NOT NULL,
                    submitted_utc  TEXT NOT NULL,
                    hidden         INTEGER NOT NULL DEFAULT 0,
                    hidden_utc     TEXT NULL,
                    hidden_reason  TEXT NULL
                );
                CREATE INDEX IF NOT EXISTS library_works_archive_active
                    ON library_works (archive_id, hidden, submitted_utc);
                CREATE TABLE IF NOT EXISTS contracts_streak (
                    user_id        TEXT PRIMARY KEY,
                    current_streak INTEGER NOT NULL DEFAULT 0,
                    best_streak    INTEGER NOT NULL DEFAULT 0,
                    last_round_id  INTEGER NOT NULL DEFAULT 0,
                    updated_utc    TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS pending_contracts_streak_folds (
                    round_id              INTEGER NOT NULL CHECK(round_id > 0),
                    user_id               TEXT NOT NULL,
                    completed_this_round  INTEGER NOT NULL CHECK(completed_this_round IN (0, 1)),
                    created_utc           TEXT NOT NULL,
                    consumed_utc          TEXT NULL,
                    PRIMARY KEY (round_id, user_id)
                );
                CREATE INDEX IF NOT EXISTS pending_contracts_streak_folds_unconsumed
                    ON pending_contracts_streak_folds (round_id)
                    WHERE consumed_utc IS NULL;
                CREATE TABLE IF NOT EXISTS rivalry_events (
                    id             INTEGER PRIMARY KEY AUTOINCREMENT,
                    attacker_guid  TEXT NOT NULL,
                    victim_guid    TEXT NOT NULL,
                    event_utc      TEXT NOT NULL
                );
                INSERT OR IGNORE INTO meta (key, value) VALUES ('current_season', 'S1');
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        // Additive migration for DBs created before standing_total existed. CREATE TABLE IF NOT EXISTS above
        // never adds a column to an already-present table, so bring old tables forward here. SQLite has no
        // ADD COLUMN IF NOT EXISTS, so we probe PRAGMA table_info first and only ALTER when the column is absent.
        await EnsureColumnAsync(conn, tx, "player_stats", "standing_total", "INTEGER NOT NULL DEFAULT 0");

        // Solreign Contracts (spec §4.1) — same additive idiom, one call per column.
        await EnsureColumnAsync(conn, tx, "player_stats", "contracts_completed", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(conn, tx, "player_stats", "contract_score", "INTEGER NOT NULL DEFAULT 0");

        // HR Points system (Beta Feedback 01, Lane B) — same additive idiom. Never redefines early_deaths
        // or any prior column; purely appends.
        await EnsureColumnAsync(conn, tx, "player_stats", "hr_points", "INTEGER NOT NULL DEFAULT 0");

        // Plaque backfill (FD-W3.5): brings v13.3-era first_death tables forward — those DBs banked
        // claims BEFORE the FD-W3 crypt wire existed, so their rows carry no crypt_reported column and
        // must default to 0 ("memorial not yet minted") for the round-start backfill to find them.
        await EnsureColumnAsync(conn, tx, "first_death", "crypt_reported", "INTEGER NOT NULL DEFAULT 0");

        // Schema v1 associates concrete round records with their player and season. Legacy rows remain
        // valid with NULL values, while new concrete IDs can be claimed exactly once per player/season.
        await EnsureColumnAsync(conn, tx, "round_log", "season_id", "TEXT");
        await EnsureColumnAsync(conn, tx, "round_log", "user_id", "TEXT");

        // Schema v2 makes a whole positive round-end replay idempotent without collapsing legitimate
        // repeated completions. The occurrence ordinal is deterministic within each submitted batch.
        await EnsureColumnAsync(conn, tx, "contract_log", "occurrence", "INTEGER");

        // Schema v3 fingerprints the complete canonical contribution. Existing v1/v2 rows intentionally keep
        // NULL hashes: a legacy replay cannot be proven exact and is rejected rather than silently mutating totals.
        await EnsureColumnAsync(conn, tx, "round_log", "payload_hash", "TEXT");

        // Station Audit inspection layer / mandatory PROVIDENCE consequence (v14 gap-closure pass).
        // Deliberately additive-only via the same EnsureColumnAsync idiom as every migration above, and
        // deliberately does NOT bump CurrentSchemaVersion: EnsureSupportedSchemaVersionAsync's guard only
        // rejects a DB whose stored user_version is NEWER than this binary's CurrentSchemaVersion — it is
        // not itself gated on CurrentSchemaVersion, so these three columns land on every DB (old or new)
        // the moment this binary opens it, version bump or not. Skipping the bump sidesteps a live
        // coordination hazard: a concurrent lane (feat/directives-fax-report) may also want to bump off
        // the same v6 baseline this pass started from, and two lanes independently claiming v7 would
        // collide. If a future lane's migration genuinely needs the stricter downgrade-protection a
        // version bump provides, bump then — this migration does not require it.
        // Antag rotation (SR-W-036 wiring): the round in which this account last held an antag role,
        // so selection can spread antag turns instead of re-picking the same few players. Additive via
        // the same EnsureColumnAsync idiom and deliberately NOT bumping CurrentSchemaVersion, for the
        // reason documented directly above: the guard only rejects a DB NEWER than this binary, so an
        // extra column is invisible to an older binary and costs no downgrade protection.
        await EnsureColumnAsync(conn, tx, "player_stats", "last_antag_round", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(conn, tx, "station_audit_log", "criteria_json", "TEXT NOT NULL DEFAULT '[]'");
        await EnsureColumnAsync(conn, tx, "station_audit_log", "checkpoint_consequence_kind", "TEXT NOT NULL DEFAULT 'none'");
        await EnsureColumnAsync(conn, tx, "station_audit_log", "checkpoint_fired_utc", "TEXT NULL");

        await using (var migration = conn.CreateCommand())
        {
            migration.Transaction = tx;
            migration.CommandText = $"""
                CREATE UNIQUE INDEX IF NOT EXISTS round_log_concrete_retry
                    ON round_log (round_id, season_id, user_id)
                    WHERE round_id > 0;
                CREATE UNIQUE INDEX IF NOT EXISTS contract_log_concrete_retry
                    ON contract_log (round_id, season_id, user_id, contract_id, scope, occurrence)
                    WHERE round_id > 0;
                -- Echo projection read: ORDER BY died_at_utc DESC, user_id ASC (tie-stable top-N).
                CREATE INDEX IF NOT EXISTS first_death_echo_projection
                    ON first_death (died_at_utc DESC, user_id);
                PRAGMA user_version = {CurrentSchemaVersion};
                """;
            await migration.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();

        _initialized = true;
    }

    private static async Task EnsureSupportedSchemaVersionAsync(
        SqliteConnection conn,
        SqliteTransaction? tx = null)
    {
        await using var version = conn.CreateCommand();
        version.Transaction = tx;
        version.CommandText = "PRAGMA user_version;";
        var storedVersion = Convert.ToInt32(await version.ExecuteScalarAsync());
        if (storedVersion > CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Season Ledger database schema {storedVersion} is newer than supported schema {CurrentSchemaVersion}.");
        }
    }

    /// <summary>
    ///     Idempotently adds <paramref name="column"/> to <paramref name="table"/> if it is not already present.
    ///     Tolerant of existing DBs (created before the column) and of fresh DBs (where the CREATE already made it).
    /// </summary>
    private static async Task EnsureColumnAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        string table,
        string column,
        string definition)
    {
        await using (var probe = conn.CreateCommand())
        {
            probe.Transaction = tx;
            probe.CommandText = $"PRAGMA table_info({table});";
            await using var reader = await probe.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                // Column 1 of table_info is the column name.
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                    return; // already there — nothing to do
            }
        }

        await using var alter = conn.CreateCommand();
        alter.Transaction = tx;
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        await alter.ExecuteNonQueryAsync();
    }

    /// <summary>Returns the current (active) season id, creating the default if absent.</summary>
    public async Task<string> GetCurrentSeasonAsync()
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            return await GetCurrentSeasonInternalAsync(conn);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static async Task<string> GetCurrentSeasonInternalAsync(SqliteConnection conn)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key = $k;";
        cmd.Parameters.AddWithValue("$k", CurrentSeasonKey);
        var result = await cmd.ExecuteScalarAsync();
        return result as string ?? DefaultSeason;
    }

    /// <summary>
    ///     Advances to a new season, archiving prior stats (they remain in the table under their old
    ///     season id). Returns the new season id. This is the operator "new season" lever.
    /// </summary>
    public async Task<string> BumpSeasonAsync()
    {
        await using var lease = await _spool.AcquireLeaseAsync(CancellationToken.None);
        if (_spool.HasUnresolved())
            throw new LedgerRecoveryBlockedException();

        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            var current = await GetCurrentSeasonInternalAsync(conn);
            var next = NextSeasonId(current);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE meta SET value = $v WHERE key = $k;";
            cmd.Parameters.AddWithValue("$v", next);
            cmd.Parameters.AddWithValue("$k", CurrentSeasonKey);
            await cmd.ExecuteNonQueryAsync();
            return next;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>"S1" -> "S2" -> ...; anything unparseable restarts at S2.</summary>
    private static string NextSeasonId(string current)
    {
        if (current.Length > 1 && current[0] == 'S' && int.TryParse(current.AsSpan(1), out var n))
            return $"S{n + 1}";
        return "S2";
    }

    /// <summary>
    ///     Folds one completed round into the player's totals for the current season and appends a
    ///     round_log entry. Upserts on (user_id, season_id). A concrete positive round ID is accepted
    ///     exactly once per player and season; non-positive IDs retain append-only legacy semantics.
    /// </summary>
    /// <summary>
    ///     Accounts whose most recent antag round is at or after <paramref name="sinceRound"/>.
    ///     Read-only and deliberately narrow: antag rotation needs recency, not the whole stat row.
    /// </summary>
    public async Task<HashSet<Guid>> GetRecentAntagsAsync(int sinceRound)
    {
        var result = new HashSet<Guid>();
        if (sinceRound <= 0)
            return result;

        await using var conn = await OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT user_id FROM player_stats WHERE last_antag_round >= $since;";
        cmd.Parameters.AddWithValue("$since", sinceRound);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (Guid.TryParse(reader.GetString(0), out var guid))
                result.Add(guid);
        }
        return result;
    }

    public async Task AddRoundRecordAsync(Guid user, RoundContribution c)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            var season = await GetCurrentSeasonInternalAsync(conn);
            var payload = SerializeRoundContribution(c, season);
            var payloadHash = HashPayload(payload);

            await using var tx = conn.BeginTransaction(deferred: false);

            if (c.RoundId > 0)
                await ThrowIfEnvelopeCommittedAsync(conn, tx, c.RoundId, CancellationToken.None);

            await using (var log = conn.CreateCommand())
            {
                log.Transaction = tx;
                log.CommandText = c.RoundId > 0
                    ? """
                        INSERT INTO round_log (round_id, season_id, user_id, ended_utc, gamemode, payload_json, payload_hash)
                        VALUES ($rid, $sid, $uid, $utc, $mode, $payload, $payload_hash)
                        ON CONFLICT(round_id, season_id, user_id) WHERE round_id > 0 DO NOTHING;
                        """
                    : """
                        INSERT INTO round_log (round_id, season_id, user_id, ended_utc, gamemode, payload_json, payload_hash)
                        VALUES ($rid, $sid, $uid, $utc, $mode, $payload, $payload_hash);
                        """;
                log.Parameters.AddWithValue("$rid", c.RoundId);
                log.Parameters.AddWithValue("$sid", season);
                log.Parameters.AddWithValue("$uid", user.ToString());
                log.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("o"));
                log.Parameters.AddWithValue("$mode", c.Gamemode ?? string.Empty);
                log.Parameters.AddWithValue("$payload", payload);
                log.Parameters.AddWithValue("$payload_hash", payloadHash);

                if (await log.ExecuteNonQueryAsync() == 0)
                {
                    await using var existing = conn.CreateCommand();
                    existing.Transaction = tx;
                    existing.CommandText = """
                        SELECT payload_hash
                        FROM round_log
                        WHERE round_id = $rid AND season_id = $sid AND user_id = $uid;
                        """;
                    existing.Parameters.AddWithValue("$rid", c.RoundId);
                    existing.Parameters.AddWithValue("$sid", season);
                    existing.Parameters.AddWithValue("$uid", user.ToString());
                    var existingHash = await existing.ExecuteScalarAsync() as string;
                    if (!string.Equals(existingHash, payloadHash, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Season Ledger conflicting replay for round {c.RoundId}, season {season}, user {user}.");
                    }

                    await tx.CommitAsync();
                    return;
                }
            }

            await using (var upsert = conn.CreateCommand())
            {
                upsert.Transaction = tx;
                upsert.CommandText = """
                    INSERT INTO player_stats (user_id, season_id, tours, captain_clean, antag_wins, early_deaths, standing_total, contracts_completed, contract_score, hr_points, last_antag_round)
                    VALUES ($uid, $sid, 1, $cap, $antag, $death, $standing, $contracts, $cscore, $hrpoints, CASE WHEN $antag > 0 THEN $round ELSE 0 END)
                    ON CONFLICT(user_id, season_id) DO UPDATE SET
                        tours = tours + 1,
                        captain_clean = captain_clean + $cap,
                        antag_wins = antag_wins + $antag,
                        early_deaths = early_deaths + $death,
                        standing_total = standing_total + $standing,
                        contracts_completed = contracts_completed + $contracts,
                        contract_score = contract_score + $cscore,
                        hr_points = hr_points + $hrpoints,
                        -- only advances on an antag round; never regresses
                        last_antag_round = CASE WHEN $antag > 0 THEN $round ELSE last_antag_round END;
                    """;
                upsert.Parameters.AddWithValue("$uid", user.ToString());
                upsert.Parameters.AddWithValue("$sid", season);
                upsert.Parameters.AddWithValue("$cap", c.WasCaptainClean ? 1 : 0);
                upsert.Parameters.AddWithValue("$antag", c.AntagWin ? 1 : 0);
                upsert.Parameters.AddWithValue("$round", c.RoundId);
                upsert.Parameters.AddWithValue("$death", c.EarlyDeath ? 1 : 0);
                upsert.Parameters.AddWithValue("$standing", c.Standing);
                upsert.Parameters.AddWithValue("$contracts", c.ContractsCompleted);
                upsert.Parameters.AddWithValue("$cscore", c.ContractScore);
                upsert.Parameters.AddWithValue("$hrpoints", c.HrPointsEarned);
                await upsert.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    private static string SerializeRoundContribution(RoundContribution contribution, string season)
    {
        return JsonSerializer.Serialize(new
        {
            captain_clean = contribution.WasCaptainClean,
            antag_win = contribution.AntagWin,
            early_death = contribution.EarlyDeath,
            round_id = contribution.RoundId,
            gamemode = contribution.Gamemode ?? string.Empty,
            standing = contribution.Standing,
            contracts_completed = contribution.ContractsCompleted,
            contract_score = contribution.ContractScore,
            hr_points_earned = contribution.HrPointsEarned,
            season,
        });
    }

    private static string HashPayload(string payload)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    /// <summary>
    ///     Returns the accumulated stats for a player in the current season. A player with no records
    ///     yields all-zero <see cref="PlayerStats"/>.
    /// </summary>
    public async Task<PlayerStats> GetStatsAsync(Guid user)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            var season = await GetCurrentSeasonInternalAsync(conn);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT tours, captain_clean, antag_wins, early_deaths, standing_total, contracts_completed, contract_score, hr_points
                FROM player_stats
                WHERE user_id = $uid AND season_id = $sid;
                """;
            cmd.Parameters.AddWithValue("$uid", user.ToString());
            cmd.Parameters.AddWithValue("$sid", season);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return new PlayerStats(0, 0, 0, 0);

            return new PlayerStats(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetInt32(7));
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    ///     Ensures a player has a row in the current season. If they already do, this is a no-op.
    ///     If they don't, it creates a row with 0 for all stats. This ensures new players
    ///     appear on the leaderboard immediately rather than waiting for their first round-end or hr point award.
    /// </summary>
    public async Task InitializePlayerStatsAsync(Guid user)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            var season = await GetCurrentSeasonInternalAsync(conn);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO player_stats (user_id, season_id, hr_points)
                VALUES ($uid, $sid, 0)
                ON CONFLICT(user_id, season_id) DO UPDATE SET hr_points = hr_points + 0;
                """;
            cmd.Parameters.AddWithValue("$uid", user.ToString());
            cmd.Parameters.AddWithValue("$sid", season);
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    ///     Awards a flat HR Points bonus to an account's CURRENT season row (creating it if the account has
    ///     no row yet — every other column then takes its schema default of 0). Used for HR Points payouts
    ///     that happen outside a round-contribution flow, namely the title-earned bonus fired from
    ///     <c>SeasonLedgerSystem.LoadTitle</c> the moment a NEW corporate title is ceremonially granted.
    ///     Non-positive awards are a no-op — the ALWAYS-CUMULATIVE model never subtracts.
    /// </summary>
    public async Task AwardHrPointsAsync(Guid user, int points)
    {
        if (points <= 0)
            return;

        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            var season = await GetCurrentSeasonInternalAsync(conn);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO player_stats (user_id, season_id, hr_points)
                VALUES ($uid, $sid, $pts)
                ON CONFLICT(user_id, season_id) DO UPDATE SET hr_points = hr_points + $pts;
                """;
            cmd.Parameters.AddWithValue("$uid", user.ToString());
            cmd.Parameters.AddWithValue("$sid", season);
            cmd.Parameters.AddWithValue("$pts", points);
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    ///     The last title that was ceremonially ANNOUNCED for this account in the CURRENT season, or null
    ///     if no ceremony has fired yet. Season-scoped on purpose: a season bump wipes the slate, so the
    ///     first earned title of a new season gets its bulletin again.
    /// </summary>
    public async Task<string?> GetAnnouncedTitleAsync(Guid user)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            var season = await GetCurrentSeasonInternalAsync(conn);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT title FROM title_grants WHERE user_id = $uid AND season_id = $sid;";
            cmd.Parameters.AddWithValue("$uid", user.ToString());
            cmd.Parameters.AddWithValue("$sid", season);

            return await cmd.ExecuteScalarAsync() as string;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    ///     Records a title as announced for this account in the current season (upsert on user/season).
    ///     Written BEFORE the bulletin dispatches so a racing double-spawn can never double-announce.
    /// </summary>
    public async Task SetAnnouncedTitleAsync(Guid user, string title)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            var season = await GetCurrentSeasonInternalAsync(conn);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO title_grants (user_id, season_id, title)
                VALUES ($uid, $sid, $title)
                ON CONFLICT(user_id, season_id) DO UPDATE SET title = $title;
                """;
            cmd.Parameters.AddWithValue("$uid", user.ToString());
            cmd.Parameters.AddWithValue("$sid", season);
            cmd.Parameters.AddWithValue("$title", title);
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    ///     Returns a player's career totals — summed across EVERY season, with no season filter — so a
    ///     rank derived from these never resets on a season bump. A player with no records yields all-zero
    ///     <see cref="PlayerStats"/>. Companion to <see cref="GetStatsAsync"/>, which is season-scoped.
    /// </summary>
    public async Task<PlayerStats> GetCareerStatsAsync(Guid user)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT
                    COALESCE(SUM(tours), 0),
                    COALESCE(SUM(captain_clean), 0),
                    COALESCE(SUM(antag_wins), 0),
                    COALESCE(SUM(early_deaths), 0),
                    COALESCE(SUM(standing_total), 0),
                    COALESCE(SUM(contracts_completed), 0),
                    COALESCE(SUM(contract_score), 0),
                    COALESCE(SUM(hr_points), 0)
                FROM player_stats
                WHERE user_id = $uid;
                """;
            cmd.Parameters.AddWithValue("$uid", user.ToString());

            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return new PlayerStats(0, 0, 0, 0);

            return new PlayerStats(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetInt32(7));
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    ///     Appends completed-contract audit rows to <c>contract_log</c> (spec §4.2). One row per completion
    ///     per contributor, stamped with the current season and UTC time. Call exactly once with the complete
    ///     round batch: occurrence ordinals are assigned within this batch. External AAR consumers receive an
    ///     separately approved future projection and never read this authoritative SQLite database directly.
    /// </summary>
    public async Task AddContractLogAsync(IReadOnlyList<ContractLogRecord> records)
    {
        if (records.Count == 0)
            return;

        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            var season = await GetCurrentSeasonInternalAsync(conn);
            var now = DateTime.UtcNow.ToString("o");

            await using var tx = conn.BeginTransaction(deferred: false);
            foreach (var roundId in records.Select(record => record.RoundId).Where(roundId => roundId > 0).Distinct())
            {
                await ThrowIfEnvelopeCommittedAsync(conn, tx, roundId, CancellationToken.None);
            }
            var occurrences = new Dictionary<(int RoundId, Guid User, string ContractId, string Scope), int>();

            foreach (var record in records)
            {
                if (string.IsNullOrWhiteSpace(record.ContractId))
                    throw new ArgumentException("Contract ID must be nonempty.", nameof(records));
                if (string.IsNullOrWhiteSpace(record.Scope))
                    throw new ArgumentException("Contract scope must be nonempty.", nameof(records));

                var key = (record.RoundId, record.User, record.ContractId, record.Scope);
                occurrences.TryGetValue(key, out var occurrence);
                occurrences[key] = occurrence + 1;

                await using var insert = conn.CreateCommand();
                insert.Transaction = tx;
                insert.CommandText = record.RoundId > 0
                    ? """
                        INSERT INTO contract_log
                            (round_id, season_id, user_id, contract_id, scope, occurrence, completed_utc)
                        VALUES ($rid, $sid, $uid, $cid, $scope, $occurrence, $utc)
                        ON CONFLICT(round_id, season_id, user_id, contract_id, scope, occurrence)
                            WHERE round_id > 0 DO NOTHING;
                        """
                    : """
                        INSERT INTO contract_log
                            (round_id, season_id, user_id, contract_id, scope, occurrence, completed_utc)
                        VALUES ($rid, $sid, $uid, $cid, $scope, $occurrence, $utc);
                        """;
                insert.Parameters.AddWithValue("$rid", record.RoundId);
                insert.Parameters.AddWithValue("$sid", season);
                insert.Parameters.AddWithValue("$uid", record.User.ToString());
                insert.Parameters.AddWithValue("$cid", record.ContractId);
                insert.Parameters.AddWithValue("$scope", record.Scope);
                insert.Parameters.AddWithValue("$occurrence", occurrence);
                insert.Parameters.AddWithValue("$utc", now);
                await insert.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    ///     Number of <c>contract_log</c> rows for an account in the current season. Read-side companion to
    ///     <see cref="AddContractLogAsync"/>; used by tests and future AAR/title tooling.
    /// </summary>
    public async Task<int> GetContractLogCountAsync(Guid user)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            var season = await GetCurrentSeasonInternalAsync(conn);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM contract_log WHERE user_id = $uid AND season_id = $sid;";
            cmd.Parameters.AddWithValue("$uid", user.ToString());
            cmd.Parameters.AddWithValue("$sid", season);

            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    ///     Completed-contract counts per round for the given round ids, from <c>contract_log</c>.
    ///     Read-only companion to <see cref="AddContractLogAsync"/> for the Shift Archive board:
    ///     the board reads its rounds from <c>station_audit_log</c> first, then asks here how many
    ///     contract completions each of those rounds logged. Rounds with no completions are simply
    ///     absent from the result. Not season-filtered — round ids are globally unique and the
    ///     archive spans season boundaries the same way <c>station_audit_log</c> does.
    /// </summary>
    public async Task<Dictionary<int, int>> GetContractCountsForRoundsAsync(IReadOnlyList<int> roundIds)
    {
        var counts = new Dictionary<int, int>();
        if (roundIds.Count == 0)
            return counts;

        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();

            await using var cmd = conn.CreateCommand();
            var parameters = new List<string>(roundIds.Count);
            for (var i = 0; i < roundIds.Count; i++)
            {
                var name = $"$r{i}";
                parameters.Add(name);
                cmd.Parameters.AddWithValue(name, roundIds[i]);
            }
            cmd.CommandText =
                $"SELECT round_id, COUNT(*) FROM contract_log WHERE round_id IN ({string.Join(", ", parameters)}) GROUP BY round_id;";

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                counts[reader.GetInt32(0)] = reader.GetInt32(1);
            return counts;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Permanently records an account-level Wingmates block. Direction is retained for auditing,
    /// while the Wingmates offer path treats a block in either direction as effective.
    /// </summary>
    /// <returns>
    /// <c>true</c> once the row is durably confirmed present (freshly inserted or already existing),
    /// <c>false</c> for a rejected self-block. Callers must treat the block as taking effect only when
    /// this returns <c>true</c> — the write is not "fire and forget".
    /// </returns>
    public async Task<bool> AddWingmateBlockAsync(Guid blocker, Guid blocked)
    {
        if (blocker == blocked)
            return false;

        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO wingmate_blocks (blocker_user_id, blocked_user_id, created_utc)
                VALUES ($blocker, $blocked, $utc)
                ON CONFLICT(blocker_user_id, blocked_user_id) DO NOTHING;
                """;
            cmd.Parameters.AddWithValue("$blocker", blocker.ToString());
            cmd.Parameters.AddWithValue("$blocked", blocked.ToString());
            cmd.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("o"));
            var inserted = await cmd.ExecuteNonQueryAsync();
            if (inserted > 0)
                return true;

            // INSERT OR IGNORE (via ON CONFLICT DO NOTHING) reports zero affected rows for an
            // already-existing block. Confirm the durable row actually exists before treating that
            // idempotent case as a failure — an existing block is still a confirmed block.
            await using var verify = conn.CreateCommand();
            verify.CommandText = """
                SELECT EXISTS(
                    SELECT 1 FROM wingmate_blocks
                    WHERE blocker_user_id = $blocker AND blocked_user_id = $blocked
                );
                """;
            verify.Parameters.AddWithValue("$blocker", blocker.ToString());
            verify.Parameters.AddWithValue("$blocked", blocked.ToString());
            return Convert.ToInt32(await verify.ExecuteScalarAsync()) == 1;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Loads the account identifiers needed to enforce Wingmates blocks in memory.</summary>
    public async Task<IReadOnlyCollection<(Guid Blocker, Guid Blocked)>> GetWingmateBlocksAsync()
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT blocker_user_id, blocked_user_id FROM wingmate_blocks;";

            var blocks = new List<(Guid Blocker, Guid Blocked)>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (!Guid.TryParse(reader.GetString(0), out var blocker) ||
                    !Guid.TryParse(reader.GetString(1), out var blocked))
                    continue;

                blocks.Add((blocker, blocked));
            }

            return blocks;
        }
        finally
        {
            _lock.Release();
        }
    }
}
