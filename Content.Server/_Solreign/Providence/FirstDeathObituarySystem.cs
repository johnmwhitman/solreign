using System;
using System.IO;
using System.Threading.Tasks;
using Content.Server._Solreign.Director;
using Content.Server.Administration.Systems;
using Content.Server.Discord;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;

namespace Content.Server._Solreign.Providence;

/// <summary>
///     FD-W4 (docs/specs/FIRST-DEATH-SPEC-2026-07-16-DRAFT.md §6): the Discord obituary leg of
///     the authored first death — BUILT INERT. Called by <c>ProvidenceFirstDeathSystem</c> exactly
///     once per account, EVER (downstream of the atomic ledger claim, beside the FD-W3 crypt leg,
///     fate-isolated from the in-game scene).
///
///     For every composed obituary, in order (write-before-dispatch):
///     <list type="number">
///     <item>Decide the gate stack — webhook gate (<see cref="CCVars.SolreignFirstDeathWebhook"/>
///     non-empty; SHIPS EMPTY = manual-paste mode, the BugReport "empty = disabled" semantics and
///     spec §9 Q1's recommended opening-weeks posture), then the player gate
///     (<see cref="CCVars.SolreignFirstDeathMinPlayers"/>, default 2 — connected players AT DEATH
///     TIME, the recap-law analogue: never advertise an empty station), then this egress's OWN
///     rate-limit channel (<see cref="RateLimitChannel"/> — the FD-W3 review lesson: any new
///     egress gets its own channel, never a shared window).</item>
///     <item>Append the obituary + the decision to <c>data/first_deaths.jsonl</c>
///     (<see cref="IResourceManager"/> user-data dir, the bug_reports.jsonl idiom — server-local,
///     excluded from shipped artifacts). EVERY claimed first death lands here regardless of the
///     gates (spec §6.2: below the player gate "the obituary goes to the JSONL file only"); the
///     line's <c>dispatch</c> field records what the stack decided.</item>
///     <item>Only if every gate passed: fire-and-forget the §8D embed via
///     <see cref="DiscordWebhook.CreateMessage"/>. With the shipping defaults this branch is
///     UNREACHABLE — the integration battery pins zero send attempts on default config.</item>
///     </list>
///
///     Confidentiality: the webhook CVar is CONFIDENTIAL and its VALUE (URL/token) is never
///     logged, in no branch — parse failures and send failures log without it. The identifier is
///     parsed locally (<see cref="FirstDeathDiscordPayload.TryParseWebhookUrl"/>, zero egress at
///     config time) rather than through the BugReport GetWebhook HTTP probe, so the ONLY network
///     touch this system can ever make is the gated send itself.
/// </summary>
public sealed partial class FirstDeathObituarySystem : EntitySystem
{
    [Dependency] private IResourceManager _res = default!;
    [Dependency] private DiscordWebhook _discord = default!;
    [Dependency] private IConfigurationManager _cfg = default!;

    /// <summary>
    ///     This egress's OWN rate-limit channel (the FD-W3 review's merge-blocker lesson,
    ///     grk r1 #1: a shared channel means a sibling's POST consumes the window and the
    ///     once-per-account-EVER send is systematically dropped with no retry). Distinct from
    ///     BOTH crypt channels — pinned by FirstDeathObituaryChannelTests.
    /// </summary>
    public const string RateLimitChannel = "first_death_discord";

    /// <summary>data/first_deaths.jsonl — the manual-paste ledger (spec §6.2).</summary>
    public const string JsonlFileName = "first_deaths.jsonl";

    private readonly object _writeLock = new();

    private string _jsonlPath = default!;
    private WebhookIdentifier? _webhookId;
    private int _minPlayers;

    public override void Initialize()
    {
        base.Initialize();

        var dir = _res.UserData.RootDir ?? Directory.GetCurrentDirectory();
        _jsonlPath = Path.Combine(dir, JsonlFileName);

        Subs.CVar(_cfg, CCVars.SolreignFirstDeathWebhook, OnWebhookChanged, invokeImmediately: true);
        Subs.CVar(_cfg, CCVars.SolreignFirstDeathMinPlayers, v => _minPlayers = v, invokeImmediately: true);
    }

    private void OnWebhookChanged(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            _webhookId = null;
            return;
        }

        if (FirstDeathDiscordPayload.TryParseWebhookUrl(url, out var id))
        {
            _webhookId = id;
            return;
        }

        _webhookId = null;
        // Never the value: the CVar is CONFIDENTIAL (URL embeds the webhook token).
        Log.Error($"{CCVars.SolreignFirstDeathWebhook.Name} is set but not a parseable Discord " +
                  "webhook URL (value withheld from log); first-death obituaries stay in " +
                  "manual-paste mode.");
    }

    /// <summary>
    ///     Entry point, called once per WON first-death claim: runs the gate stack, appends the
    ///     JSONL line (always, write-before-dispatch), then — only if every gate passed —
    ///     invokes the send path. Synchronous except the fire-and-forget send; a caller-side
    ///     try/catch in <c>ProvidenceFirstDeathSystem</c> fate-isolates it from the scene.
    /// </summary>
    public void Record(FirstDeathObituary obituary)
    {
        var dispatch = Decide(obituary);

        // Write BEFORE dispatch: the file is the durable artifact; the send is best-effort.
        WriteToJsonl(FirstDeathObituaryJsonl.ToLine(obituary, dispatch));

        if (dispatch != FirstDeathObituaryDispatch.Dispatched || _webhookId is not { } webhookId)
            return;

        SendToDiscord(webhookId, FirstDeathDiscordPayload.Build(obituary));
    }

    /// <summary>
    ///     The gate stack, in order. The rate-limit window is only consumed AFTER the cheaper
    ///     gates pass — a webhook-empty or under-populated obituary must never burn the channel's
    ///     window for a later send.
    /// </summary>
    private FirstDeathObituaryDispatch Decide(FirstDeathObituary obituary)
    {
        if (_webhookId is null)
            return FirstDeathObituaryDispatch.WebhookEmpty;

        if (!FirstDeathDiscordPayload.MeetsPlayerGate(obituary.PlayerCountAtDeath, _minPlayers))
            return FirstDeathObituaryDispatch.BelowPlayerGate;

        if (!DirectorChannel.TryEnterRateLimit(RateLimitChannel))
            return FirstDeathObituaryDispatch.RateLimited;

        return FirstDeathObituaryDispatch.Dispatched;
    }

    private void WriteToJsonl(string line)
    {
        try
        {
            lock (_writeLock)
            {
                File.AppendAllText(_jsonlPath, line + "\n");
            }
        }
        catch (Exception e)
        {
            Log.Error($"Error writing first-death obituary to {_jsonlPath}:\n{e}");
        }
    }

    /// <summary>
    ///     The one egress path (BugReport threading contract: async void + try/catch, never blocks
    ///     the tick). Every send increments the attempt counter FIRST so the default-config
    ///     zero-egress law is assertable; the error path logs without the URL or token.
    /// </summary>
    private async void SendToDiscord(WebhookIdentifier webhookId, WebhookPayload payload)
    {
        _discordSendAttempts++;

        try
        {
            if (_sendOverrideForTests is { } mockSend)
            {
                await mockSend(payload);
                return;
            }

            await _discord.CreateMessage(webhookId, payload);
        }
        catch (Exception e)
        {
            // Exception text from HttpClient can embed the request URI (the webhook token):
            // log the TYPE only, never the message, never the URL.
            Log.Error($"Error sending first-death obituary to Discord webhook: {e.GetType().Name} (details withheld: confidential URL)");
        }
    }

    // --- Test seams (the ProvidenceWelcomeSystem idiom) ----------------------------------------------

    private int _discordSendAttempts;
    private Func<WebhookPayload, Task>? _sendOverrideForTests;

    /// <summary>How many times the send path has been entered — the default-config zero-egress
    /// proof asserts this stays 0; the both-gates-met scenario asserts exactly 1.</summary>
    internal int DiscordSendAttemptsForTests => _discordSendAttempts;

    /// <summary>Whether the webhook CVar parsed into a usable identifier.</summary>
    internal bool WebhookConfiguredForTests => _webhookId is not null;

    /// <summary>Absolute path of the JSONL ledger this system appends to.</summary>
    internal string JsonlPathForTests => _jsonlPath;

    /// <summary>Redirects the JSONL ledger to a per-test temp file (the SeasonLedgerDbPath law:
    /// tests never write the real server data location).</summary>
    internal void SetJsonlPathForTests(string path) => _jsonlPath = path;

    /// <summary>Replaces the real Discord HTTP call — tests NEVER egress, not even at a fake
    /// URL. Null restores the real sender.</summary>
    internal void SetSendOverrideForTests(Func<WebhookPayload, Task>? send) => _sendOverrideForTests = send;

    /// <summary>Zeroes the attempt counter for a fresh scenario.</summary>
    internal void ResetSendAttemptsForTests() => _discordSendAttempts = 0;
}
