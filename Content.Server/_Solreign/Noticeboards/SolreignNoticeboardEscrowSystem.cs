#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Server._Solreign.Noticeboards;

/// <summary>
///     SR-W-066: SS14 Solreign Dynamic Station Noticeboard & Broadcast Escrow System.
///     Tracks station broadcast notices, flyer pinning fees, urgent emergency announcements,
///     notice decay rates, and CVar thresholds (<c>solreign.noticeboard_escrow_enabled</c>).
/// </summary>
public enum NoticePriority : byte
{
    Standard = 0,
    Flyer = 1,
    Broadcast = 2,
    EmergencyUrgent = 3,
}

public sealed record EscrowNoticeEntry
{
    public int Id { get; init; }
    public string Author { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public NoticePriority Priority { get; init; }
    public int EscrowFee { get; init; }
    public bool IsUrgent { get; init; }
    public DateTime CreatedAt { get; init; }
    public TimeSpan Duration { get; init; }
    public float Health { get; set; } = 100.0f;
    public bool IsActive { get; set; } = true;
}

public sealed partial class SolreignNoticeboardEscrowSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;

    private bool _enabled = true;
    private int _basePinFee = 50;
    private float _urgentMultiplier = 2.5f;
    private int _broadcastMinEscrow = 200;
    private float _decayRatePerHour = 10.0f;

    private int _nextId = 1;
    private readonly List<EscrowNoticeEntry> _notices = new();

    public bool IsEnabled => _enabled;
    public int BasePinFee => _basePinFee;
    public float UrgentMultiplier => _urgentMultiplier;
    public int BroadcastMinEscrow => _broadcastMinEscrow;
    public float DecayRatePerHour => _decayRatePerHour;

    public override void Initialize()
    {
        base.Initialize();

        if (_config != null)
        {
            Subs.CVar(_config, CCVars.SolreignNoticeboardEscrowEnabled, v => _enabled = v, invokeImmediately: true);
            Subs.CVar(_config, CCVars.SolreignNoticeboardEscrowBasePinFee, v => _basePinFee = v, invokeImmediately: true);
            Subs.CVar(_config, CCVars.SolreignNoticeboardEscrowUrgentMultiplier, v => _urgentMultiplier = v, invokeImmediately: true);
            Subs.CVar(_config, CCVars.SolreignNoticeboardEscrowBroadcastMinEscrow, v => _broadcastMinEscrow = v, invokeImmediately: true);
            Subs.CVar(_config, CCVars.SolreignNoticeboardEscrowDecayRatePerHour, v => _decayRatePerHour = v, invokeImmediately: true);
        }
    }

    /// <summary>
    ///     Directly configure parameters (for standalone unit tests or system initialization without full IoC).
    /// </summary>
    public void SetConfigValues(bool enabled, int basePinFee, float urgentMultiplier, int broadcastMinEscrow, float decayRatePerHour)
    {
        _enabled = enabled;
        _basePinFee = basePinFee;
        _urgentMultiplier = urgentMultiplier;
        _broadcastMinEscrow = broadcastMinEscrow;
        _decayRatePerHour = decayRatePerHour;
    }

    /// <summary>
    ///     Calculates required fee in credits for pinning or broadcasting a notice.
    /// </summary>
    public int CalculatePinFee(NoticePriority priority, bool isUrgent, TimeSpan? customDuration = null)
    {
        var baseFee = priority switch
        {
            NoticePriority.Standard => _basePinFee,
            NoticePriority.Flyer => _basePinFee,
            NoticePriority.Broadcast => (_basePinFee * 2) + _broadcastMinEscrow,
            NoticePriority.EmergencyUrgent => (_basePinFee * 3) + _broadcastMinEscrow,
            _ => _basePinFee,
        };

        var fee = (double) baseFee;

        if (customDuration.HasValue && customDuration.Value.TotalHours > 1.0)
        {
            fee *= customDuration.Value.TotalHours;
        }

        if (isUrgent || priority == NoticePriority.EmergencyUrgent)
        {
            fee *= _urgentMultiplier;
        }

        return (int) Math.Ceiling(fee);
    }

    /// <summary>
    ///     Attempts to pin or broadcast a new notice under escrow rules.
    /// </summary>
    public bool TryPinNotice(
        string author,
        string title,
        string content,
        NoticePriority priority,
        int feePaid,
        bool isUrgent,
        TimeSpan duration,
        out EscrowNoticeEntry? notice,
        out string error)
    {
        notice = null;

        if (!_enabled)
        {
            error = "Noticeboard escrow system is currently disabled.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(content))
        {
            error = "Notice title and content cannot be empty.";
            return false;
        }

        if (duration <= TimeSpan.Zero)
        {
            error = "Notice duration must be positive.";
            return false;
        }

        var requiredFee = CalculatePinFee(priority, isUrgent, duration);
        if (feePaid < requiredFee)
        {
            error = $"Insufficient escrow fee. Paid: {feePaid}, Required: {requiredFee}.";
            return false;
        }

        var entry = new EscrowNoticeEntry
        {
            Id = _nextId++,
            Author = string.IsNullOrWhiteSpace(author) ? "Anonymous" : author.Trim(),
            Title = title.Trim(),
            Content = content.Trim(),
            Priority = priority,
            EscrowFee = feePaid,
            IsUrgent = isUrgent || priority == NoticePriority.EmergencyUrgent,
            CreatedAt = DateTime.UtcNow,
            Duration = duration,
            Health = 100.0f,
            IsActive = true,
        };

        _notices.Add(entry);
        notice = entry;
        error = string.Empty;
        return true;
    }

    /// <summary>
    ///     Returns active notices ordered by priority descending, urgency, escrow fee descending, and creation time descending.
    /// </summary>
    public IReadOnlyList<EscrowNoticeEntry> GetSortedNotices()
    {
        return _notices
            .Where(n => n.IsActive && n.Health > 0.0f)
            .OrderByDescending(n => (int) n.Priority)
            .ThenByDescending(n => n.IsUrgent)
            .ThenByDescending(n => n.EscrowFee)
            .ThenByDescending(n => n.CreatedAt)
            .ToList();
    }

    /// <summary>
    ///     Simulates or applies time decay to active notices.
    /// </summary>
    public void UpdateDecay(TimeSpan elapsedTime)
    {
        if (elapsedTime <= TimeSpan.Zero)
            return;

        var hours = (float) elapsedTime.TotalHours;
        var decayAmount = _decayRatePerHour * hours;

        foreach (var notice in _notices)
        {
            if (!notice.IsActive)
                continue;

            notice.Health = Math.Max(0.0f, notice.Health - decayAmount);

            if (notice.Health <= 0.0f)
            {
                notice.IsActive = false;
            }
        }
    }

    /// <summary>
    ///     Returns active broad-channel station broadcasts and urgent emergency announcements.
    /// </summary>
    public IReadOnlyList<EscrowNoticeEntry> GetStationBroadcasts()
    {
        return _notices
            .Where(n => n.IsActive && n.Health > 0.0f && (n.Priority == NoticePriority.Broadcast || n.Priority == NoticePriority.EmergencyUrgent || n.IsUrgent))
            .OrderByDescending(n => (int) n.Priority)
            .ThenByDescending(n => n.CreatedAt)
            .ToList();
    }

    /// <summary>
    ///     Calculates total escrow balance stored across all active notices.
    /// </summary>
    public int GetTotalEscrowBalance()
    {
        return _notices.Where(n => n.IsActive && n.Health > 0.0f).Sum(n => n.EscrowFee);
    }

    /// <summary>
    ///     Returns active notice count.
    /// </summary>
    public int GetActiveNoticeCount()
    {
        return _notices.Count(n => n.IsActive && n.Health > 0.0f);
    }

    /// <summary>
    ///     Retrieves a notice by ID.
    /// </summary>
    public EscrowNoticeEntry? GetNoticeById(int id)
    {
        return _notices.FirstOrDefault(n => n.Id == id);
    }

    /// <summary>
    ///     Cancels a notice and returns prorated escrow refund.
    /// </summary>
    public bool CancelNotice(int id, out int refundedEscrow)
    {
        refundedEscrow = 0;
        var notice = GetNoticeById(id);
        if (notice == null || !notice.IsActive)
            return false;

        refundedEscrow = (int) Math.Floor(notice.EscrowFee * (notice.Health / 100.0f));
        notice.IsActive = false;
        return true;
    }

    /// <summary>
    ///     Purges expired and inactive notices from memory.
    /// </summary>
    public void PurgeExpiredNotices()
    {
        _notices.RemoveAll(n => !n.IsActive || n.Health <= 0.0f);
    }

    /// <summary>
    ///     Resets internal state (useful for unit tests).
    /// </summary>
    public void ResetState()
    {
        _nextId = 1;
        _notices.Clear();
    }
}
