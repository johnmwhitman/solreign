using System;
using System.Collections.Generic;
using System.Numerics;
using Robust.Shared.Map;

namespace Content.Server._Solreign.Social;

/// <summary>
///     Pure round-local "chirp answered" pairing logic (no ECS, no clock, no storage — unit-tested
///     in Content.Tests/_Solreign/ChirpAnswerTrackerTests.cs, same thin-glue split as
///     <c>FirstDeathRoundGuard</c>/<c>ProvidenceWelcomeGate</c>).
///
///     Rule: when account B chirps within <see cref="_window"/> of account A's chirp, on the same
///     map and within <see cref="_maxRange"/> of where A chirped, A's chirp counts as ANSWERED —
///     A is the account whose greeting got returned, so A is who the "first chirp answered"
///     milestone belongs to. One pending chirp per account (a re-chirp refreshes it); an answered
///     chirp is consumed, so one reply can never report the same pending chirp twice — but one
///     reply CAN answer several distinct pending chirpers at once (three people chirp, a fourth
///     replies: all three were answered). The answerer's own chirp is then recorded as pending, so
///     round-robin greetings chain naturally.
///
///     This tracker never decides once-per-account-EVER — that is the social_firsts claim row's
///     job. It only decides "did a reply just happen".
/// </summary>
public sealed class ChirpAnswerTracker
{
    private readonly TimeSpan _window;
    private readonly float _maxRange;

    private readonly Dictionary<Guid, PendingChirp> _pending = new();

    private readonly record struct PendingChirp(MapId Map, Vector2 Position, TimeSpan At);

    public ChirpAnswerTracker(TimeSpan window, float maxRange)
    {
        _window = window;
        _maxRange = maxRange;
    }

    /// <summary>Pending not-yet-answered chirps — test visibility only.</summary>
    public int PendingCount => _pending.Count;

    /// <summary>
    ///     Records one chirp and returns every account whose pending chirp this one just answered
    ///     (possibly empty, never null). Answered entries are consumed; the chirper's own entry is
    ///     always (re)recorded afterwards so their greeting is answerable in turn.
    /// </summary>
    public IReadOnlyList<Guid> RecordChirp(Guid user, MapId map, Vector2 position, TimeSpan now)
    {
        Prune(now);

        List<Guid>? answered = null;
        List<Guid>? consumed = null;

        foreach (var (other, chirp) in _pending)
        {
            if (other == user)
                continue;

            if (chirp.Map != map)
                continue;

            if ((chirp.Position - position).Length() > _maxRange)
                continue;

            answered ??= new List<Guid>();
            consumed ??= new List<Guid>();
            answered.Add(other);
            consumed.Add(other);
        }

        if (consumed != null)
        {
            foreach (var other in consumed)
            {
                _pending.Remove(other);
            }
        }

        _pending[user] = new PendingChirp(map, position, now);

        return (IReadOnlyList<Guid>?) answered ?? Array.Empty<Guid>();
    }

    /// <summary>Drops every pending chirp older than the answer window.</summary>
    public void Prune(TimeSpan now)
    {
        List<Guid>? stale = null;
        foreach (var (user, chirp) in _pending)
        {
            if (now - chirp.At > _window)
            {
                stale ??= new List<Guid>();
                stale.Add(user);
            }
        }

        if (stale == null)
            return;

        foreach (var user in stale)
        {
            _pending.Remove(user);
        }
    }

    /// <summary>Round-boundary reset — a chirp must never be answered across rounds.</summary>
    public void Clear()
    {
        _pending.Clear();
    }
}
