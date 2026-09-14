using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._Solreign.PlayerDelight.FirstShift;
using Robust.Shared.Network;

namespace Content.Server._Solreign.PlayerDelight.FirstShift;

public readonly record struct FirstShiftAssignment(
    FirstShiftDepartment Department,
    string CardId,
    FirstShiftAssignmentStage Stage,
    uint Generation,
    TimeSpan NextRerollAt);

public readonly record struct FirstShiftCounters(int Active, int Completed, int Rerolled);

public readonly record struct FirstShiftTransitionResult(
    bool Changed,
    string Reason,
    FirstShiftAssignment? Assignment = null);

/// <summary>
/// Pure, round-local First Shift reducer. It owns no persistence and selection never consumes identity data.
/// </summary>
public sealed class FirstShiftRoundState
{
    public static readonly TimeSpan RerollCooldown = TimeSpan.FromSeconds(2);

    private readonly Dictionary<NetUserId, FirstShiftAssignment> _assignments = new();
    private readonly Dictionary<NetUserId, uint> _generationClocks = new();
    private int _completed;
    private int _rerolled;

    public FirstShiftCounters Counters => new(_assignments.Count, _completed, _rerolled);
    public IEnumerable<NetUserId> ActiveUsers => _assignments.Keys;

    public FirstShiftTransitionResult Request(
        NetUserId user,
        FirstShiftDepartment department,
        IReadOnlyCollection<FirstShiftCard> catalog,
        ulong roundSeed,
        TimeSpan now)
    {
        if (!IsDepartment(department))
            return new(false, "invalid-department");

        var selected = SelectCard(roundSeed, department, catalog, null);
        if (selected is null)
            return new(false, "no-eligible-assignment");

        if (_assignments.TryGetValue(user, out var existing) &&
            existing.Department == department && existing.CardId == selected.Value.Id)
            return new(false, "already-assigned", existing);

        var generation = NextGeneration(user);
        var assignment = new FirstShiftAssignment(
            department,
            selected.Value.Id,
            FirstShiftAssignmentStage.Assigned,
            generation,
            now);
        _assignments[user] = assignment;
        return new(true, "assigned", assignment);
    }

    public FirstShiftTransitionResult Reroll(
        NetUserId user,
        IReadOnlyCollection<FirstShiftCard> catalog,
        ulong roundSeed,
        TimeSpan now)
    {
        if (!_assignments.TryGetValue(user, out var existing))
            return new(false, "not-assigned");
        if (now < existing.NextRerollAt)
            return new(false, "reroll-throttled", existing);

        var selected = SelectCard(roundSeed, existing.Department, catalog, existing.CardId);
        if (selected is null)
            return new(false, "no-eligible-assignment", existing);
        if (selected.Value.Id == existing.CardId)
            return new(false, "no-alternative", existing);

        var assignment = existing with
        {
            CardId = selected.Value.Id,
            Stage = FirstShiftAssignmentStage.Assigned,
            Generation = NextGeneration(user),
            NextRerollAt = now + RerollCooldown,
        };
        _assignments[user] = assignment;
        _rerolled++;
        return new(true, "rerolled", assignment);
    }

    public FirstShiftTransitionResult Advance(NetUserId user, FirstShiftAssignmentStage requestedStage)
    {
        if (!_assignments.TryGetValue(user, out var existing))
            return new(false, "not-assigned");
        if (!Enum.IsDefined(requestedStage))
            return new(false, "invalid-stage", existing);
        if (requestedStage == existing.Stage)
            return new(false, "stage-unchanged", existing);
        if (requestedStage < existing.Stage)
            return new(false, "stale-stage", existing);
        if ((int) requestedStage != (int) existing.Stage + 1)
            return new(false, "stage-skip", existing);

        var assignment = existing with { Stage = requestedStage, Generation = NextGeneration(user) };
        _assignments[user] = assignment;
        return new(true, "advanced", assignment);
    }

    public FirstShiftTransitionResult Advance(NetUserId user)
    {
        if (!_assignments.TryGetValue(user, out var existing))
            return new(false, "not-assigned");
        // MG-W3 (spec §3.6 edit #2): Mark is the appended terminal stage — Debrief now advances
        // INTO Mark like any other stage; only Mark itself is the "nowhere left to go" terminus.
        if (existing.Stage == FirstShiftAssignmentStage.Mark)
            return new(false, "already-at-mark", existing);
        return Advance(user, existing.Stage + 1);
    }

    public bool IsCurrentGeneration(NetUserId user, uint generation) =>
        _assignments.TryGetValue(user, out var assignment) && assignment.Generation == generation;

    public void ClearActive()
    {
        foreach (var user in _assignments.Keys.ToArray())
            Clear(user);
    }

    public FirstShiftTransitionResult Complete(NetUserId user)
    {
        if (!_assignments.TryGetValue(user, out var existing))
            return new(false, "not-assigned");
        // MG-W3 (spec §3.6 edit #2): Complete now requires Mark, not Debrief. The dormant-ship
        // posture (Mark unreachable while solreign.mark.enabled is false) is an ADAPTER-level
        // concern (FirstShiftSystem.OnComplete's 2-line shim), not a pure-reducer one — this law
        // is unconditional so the reducer never needs to know about the CVar.
        if (existing.Stage != FirstShiftAssignmentStage.Mark)
            return new(false, "not-at-mark", existing);

        _assignments.Remove(user);
        NextGeneration(user);
        _completed++;
        return new(true, "completed", existing);
    }

    public FirstShiftTransitionResult Clear(NetUserId user)
    {
        if (!_assignments.Remove(user, out var existing))
            return new(false, "not-assigned");
        NextGeneration(user);
        return new(true, "cleared", existing);
    }

    public void Disconnect(NetUserId user)
    {
        if (_assignments.Remove(user))
            NextGeneration(user);
    }

    public void ClearRound()
    {
        _assignments.Clear();
        _generationClocks.Clear();
        _completed = 0;
        _rerolled = 0;
    }

    public FirstShiftAssignment? GetAssignment(NetUserId user) =>
        _assignments.TryGetValue(user, out var assignment) ? assignment : null;

    /// <summary>
    /// Stable selection API deliberately contains no user/account/name/history argument.
    /// </summary>
    public static FirstShiftCard? SelectCard(
        ulong roundSeed,
        FirstShiftDepartment department,
        IEnumerable<FirstShiftCard> catalog,
        string? previousCardId)
    {
        if (!IsDepartment(department))
            return null;

        var candidates = Eligible(catalog, department).ToArray();
        if (candidates.Length == 0 && department != FirstShiftDepartment.Universal)
            candidates = Eligible(catalog, FirstShiftDepartment.Universal).ToArray();
        if (candidates.Length == 0)
            return null;

        var withoutPrevious = candidates
            .Where(card => !string.Equals(card.Id, previousCardId, StringComparison.Ordinal))
            .ToArray();
        var pool = withoutPrevious.Length > 0 ? withoutPrevious : candidates;
        var hash = StableHash(roundSeed, department, pool);
        return pool[(int) (hash % (uint) pool.Length)];
    }

    private static IEnumerable<FirstShiftCard> Eligible(
        IEnumerable<FirstShiftCard> catalog,
        FirstShiftDepartment department) => catalog
        .Where(card => card.Enabled &&
                       card.Department == department &&
                       card.SafetyClassification == FirstShiftSafetyClassification.SafeOrientation &&
                       !string.IsNullOrWhiteSpace(card.Id))
        .OrderBy(card => card.Id, StringComparer.Ordinal);

    private static bool IsDepartment(FirstShiftDepartment department) => Enum.IsDefined(department);

    private uint NextGeneration(NetUserId user)
    {
        var next = _generationClocks.GetValueOrDefault(user) + 1;
        _generationClocks[user] = next;
        return next;
    }

    private static uint StableHash(
        ulong roundSeed,
        FirstShiftDepartment department,
        IReadOnlyCollection<FirstShiftCard> cards)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;
        var hash = offset;
        for (var shift = 0; shift < 64; shift += 8)
        {
            hash ^= (byte) (roundSeed >> shift);
            hash *= prime;
        }
        hash ^= (byte) department;
        hash *= prime;
        foreach (var card in cards)
        {
            foreach (var character in card.Id)
            {
                hash ^= character;
                hash *= prime;
            }
            hash ^= 0xff;
            hash *= prime;
        }
        return hash;
    }
}
