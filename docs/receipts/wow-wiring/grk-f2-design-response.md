## Spec judgment — Acid Storm

### 1. Sustained visual: (a), not (b)

**One opening screen-fx sting at `Started()` only.** Same pattern as Solar Flare. Hold it at the cap (~3–5s, whatever Solar Flare uses).

Why not periodic re-trigger:
- Shader is a **momentary brand sting**, not ambient weather. Re-firing every N seconds for 1–2 minutes turns a border pulse into a seizure-adjacent metronome.
- Overlap = extend-hold, so a short interval becomes a near-permanent border glow (reads as UI bug / status effect, not weather).
- Atmosphere for 60–120s should come from: **announcement text + Providence start VO + particle scatter + optional end announcement**. Particles are the sustained visual; the sting is the “it begins” hit.

If you later want mid-event reinforcement, do it once at ~halfway with a **shorter** duration (e.g. 2s), not a loop. Not for v1.

### 2. Damage: zero this wave — correct, not a cop-out

Match Solar Flare / Spore Drift: **pure atmosphere**.

- No breach concept → any “outside/near breach” hazard is greenfield systems work.
- “Mild hazard” in a brief is permission, not a requirement; both siblings already chose cosmetic/light infra.
- Document in the rule file header (or adjacent comment in YAML): *v1 atmosphere only; mechanical hazard deferred.*

Deferred wave (when you want teeth): station exterior tiles / space-adjacent only, tiny toxin or burn tick, or splash damage on unsealed airlocks — still no full “weather system.” Not now.

### 3. All-clear: text `endAnnouncement` only — correct

Do **not** replay `EventAcidStorm` VO at end. Those lines are storm-arrival.

| Moment | Audio | Text |
|--------|--------|------|
| Start | `startAudio` chime + `_providence.PlayLine(EventAcidStorm)` | `startAnnouncement` |
| End | optional generic `endAudio` chime only (or none — match siblings) | `endAnnouncement` (“conditions normalizing…”) |

Honest given the asset inventory. Reusing start lines as clear is worse than silence.

### 4. CVar: `ForceEndSelf` in `Started()` — right idiom

Admin-only, no pre-scheduler gate → only place you control is the rule itself.

```text
Started():
  if (!_cfg.GetCVar(CCVars.SolreignAcidStormEnabled)) { ForceEndSelf(uid, gameRule); return; }
  // ... real start work
```

- `SERVERONLY`, default **true**, own partial CCVars next to other Solreign rule CVars.
- ForceEndSelf still runs base `Ended` / end announcement path depending on engine timing — **verify** whether a same-tick ForceEndSelf still fires end announcement. If it does, either skip announcements when disabled (if the base allows) or accept a one-line no-op end; document the observed behavior after one admin test.
- Prefer ForceEndSelf over “silently do nothing for 120s” so the event doesn’t look “started” in admin tooling while inert.

### 5. Failure modes

| Mode | Risk | Mitigation |
|------|------|------------|
| **Double-start / stack** | Second rule while first runs: second sting + more particles + second VO; if you ever mutate shared state in `Ended()`, last end wins | v1 has **no restore state** (unlike APC flicker) → stack is **cosmetic only**. Optional: if `GameRuleSystem` has “already active” / query running rules of same type, early ForceEndSelf the new one. Prefer **allow stack** for admin-startable weather (rare) over inventing mutex unless double-start is common. Document: “overlapping storms stack particles/stings; no shared mutable state.” |
| **Ghost/observer** | Screen-fx is station-wide broadcast | **Fine** for weather. Weather is environment, not a personal address. Don’t invent ghost filtering. |
| **Mid-round join** | Misses sting + past particles; still sees remaining particles if any; may miss VO | Acceptable for atmospheric event. Particles should use **TimedDespawn** long enough to feel present but not permanent (see below). No join-sync of screen-fx needed. |
| **PG-13 / “acid”** | Body-horror read | Frame as **industrial chemical weather / scrubber failure / exterior decon plume**, not flesh-melting. Copy: corporate euphemism (“external atmospheric contaminant event,” “neutralizing agents deployed”). Visual: **acid-green mote tint** (reuse `SolreignSporeParticleComponent`), not skin effects, not screamers, not puddles of melting. Screen border only. VO: existing staged lines only. |

**Spam (admin hammer):** each start = VO + sting + particle wave. Cap particle count hard (mood preferred vs normal like Spore Drift). Duration 60–120s via `duration`/`maxDuration` only — no custom timer.

### Concrete shape (implement this)

Mirror Spore Drift + Solar Flare start sting + Providence:

| Piece | Value |
|--------|--------|
| Prototype id | `SolreignAcidStorm` |
| Table | Add to `SolreignWeatherEventsTable` only |
| Weight | ~6–8 (between rare and normal; slightly less common than “fun light show” if both are weight 8) |
| Duration | `duration: 60`, `maxDuration: 120` |
| Start | `startAnnouncement` Loc + `startAudio: attention.ogg` + `_providence.PlayLine(EventAcidStorm)` + one `SolreignScreenFxEvent` (~3–5s) + scatter `SolreignAmbientAcidDroplet` (or similar) reusing **same** particle component, toxic-green tint |
| End | `endAnnouncement` only; no Providence line; no second sting |
| Particles | Preferred vs normal count via `_mood.IsWeatherEventPreferred`; TimedDespawn **longer than Spore’s 8s** so they persist across the weather window (e.g. **20–40s** lifetime, respawn wave optional — **v1: one scatter at start**, not continuous spawn, keeps ActiveTick empty and avoids spam) |
| Damage | None |
| CVar | `SolreignAcidStormEnabled` default true; ForceEndSelf if false |
| Component | `SolreignAcidStormRule` / `SolreignAcidStormRuleComponent` with ScreenFxDuration, SporeCount/Preferred, effect prototype id |

**One scatter at start, no ActiveTick loop** unless you later add a single mid-pulse. Keeps the rule as dumb as Spore Drift with a longer StationEvent duration.

### 6. Other flags before implement

1. **Particle lifetime vs event duration:** 8s motes on a 120s event feel like a false start. Either longer despawn **or** 1–2 extra scatter pulses (e.g. at Started + optional second at half duration with no extra VO/sting). Prefer longer lifetime first (simpler).
2. **Don’t hang shared mutable state on the rule** so double-start/`Ended` can’t clobber (you’re not doing APC restore — good).
3. **Loc tone:** start = Providence-adjacent corporate calm; end = “external contaminant levels within parameters” — never “your skin is dissolving.”
4. **Don’t touch** upstream `events.yml`, screen-fx max, or Providence sound registration (already done).
5. **Verify ForceEndSelf + announcements** once with CVar false so admins don’t get a fake all-clear on a disabled start.
6. **Admin discoverability:** same as siblings — `event start SolreignAcidStorm`; optional note in event Loc that it’s weather-table only.
7. **Performance:** particle count budget like Spore Drift; no physics, no lights on every mote if Spore already uses PointLight — match Spore’s light cost or drop light if count is high.

### Verdict summary

| Q | Call |
|---|------|
| Screen-fx | **(a)** one open sting |
| Damage | **Zero** this wave |
| All-clear | **Text only** |
| CVar | **ForceEndSelf in Started** |
| Stack / ghosts / late join | Document; no mutex required; ghosts OK; late join OK |

Ship atmosphere first; hazard is a later PR with real exterior/airlock hooks. Ready for your diff review when you have it.
