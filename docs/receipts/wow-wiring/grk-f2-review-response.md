I'll review the full diff against the prior verdict, starting from the complete offloaded prompt.Checking sibling rules and `ForceEndSelf`/`StationEventSystem` lifecycle so the review is grounded in this codebase, not guesswork.## Verdict: **ship with one must-fix**

You followed the prior design on the core behavior path. One CI orphan, one CVar kill-switch lifecycle gap, a few soft nits.

---

### 1. `Started` vs prior verdict — **match**

| Verdict | Diff |
|---|---|
| One opening screen-fx | Single `RaiseNetworkEvent(new SolreignScreenFxEvent(...))` — no pulse / no `ActiveTick` |
| Zero damage | No damage/hazard code |
| Text-only all-clear | No `Ended` override; base `StationEventSystem.Ended` owns end text |
| CVar → `ForceEndSelf` before side effects | Gate is after `base.Started`, before FX/VO/spawn + early `return` |
| Double-start OK | No restore state (unlike Solar Flare APC list) |
| Broadcast ghosts OK | Unfiltered `RaiseNetworkEvent` (same as Solar Flare) |
| Reuse spore particle system | Spawns `SolreignAcidMote` with `SolreignSporeParticle` only |

Shape mirrors `SolreignSporeDriftRule` + Solar Flare sting/VO. Good.

---

### 2. `weight: 3` — **correct intent**

Mission “rare” beats my earlier 6–8 suggestion. Relative to SolarFlare=8 / SporeDrift=6, **3 is intentionally rarer** — right for a “wow” beat that should not go stale.

Note: weight is inert until something actually weighted-picks from `SolreignWeatherEventsTable` / events tables (still admin-startable only today). Staging is fine.

---

### 3. Mote lifetime 30s vs event 60–120s — **acceptable**

One scatter + empty `ActiveTick` was the right call. Fade-before-all-clear is a good weather read.

**Doc nit only:** `spores.yml` claims motes stay visible “across the storm’s much longer 60–120s window” — false for 30s. Soften that comment so it doesn’t lie.

---

### 4. `ForceEndSelf` side-effect semantics — **partial**

**Your Started-side effects: correctly blocked.**

```csharp
ForceEndSelf(...);
return;  // never reaches RaiseNetworkEvent / PlayLine / Spawn
```

`ForceEndSelf` → `GameTicker.EndGameRule` — does **not** continue the rest of `Started`. Integration test that `IsGameRuleActive` is false is valid for “not inert for 60–120s.”

**But lifecycle leaks remain** (inherent to gate-in-`Started`):

| Phase | When CVar=false |
|---|---|
| `StationEventSystem.Added` (before `Started`) | **Still fires** `startAnnouncement` + `attention.ogg` |
| Your `Started` FX/VO/motes | **Blocked** by early return |
| `ForceEndSelf` → `Ended` | **Still fires** `endAnnouncement` |

So CVar-off admin-start yields a false start + immediate all-clear with no FX/VO/motes. Test copy that claims “no announcement effects” is **overclaim**.

Not a violation of “gate in `Started`” (that was the verdict), but a real kill-switch gap if you want silent disable.

**If you want silent kill switch (recommended before commit if easy):** in the CVar-off branch, null `StartAnnouncement`/`EndAnnouncement`/`StartAudio`/`EndAudio` on `StationEventComponent` **before** `ForceEndSelf`, *or* override `Added` to skip `base.Added` when off *and* clear end announcement before end. Started-only gate cannot stop `Added`.

---

### 5. Tone (loc) — **pass, one soft edge**

| Line | Read |
|---|---|
| start: “external contaminant plume… Neutralizing agents deployed; minor visual haze… Solreign appreciates your patience” | Corporate euphemism: solid |
| end: “standard parameters… continued cooperation” | Sinister-corporate: solid |
| popup: “acid-green condensation… **hissing faintly against the deck plating**” | Mildly more physical than the announcements; still PG-13, not melt/burn |

Optional soft: drop “hissing… deck plating” if you want full alignment with announcement restraint. Not a hard fail.

---

### 6. Component idiom — **correct reuse**

`[Access(typeof(SolreignAcidStormRule))]` + `EntProtoId MoteEffectPrototype = "SolreignAcidMote"` matches SporeDrift. No issue.

---

### 7. Other findings

#### **MUST FIX — orphan allowlist**

`SolreignAcidMote` is only referenced from C# (`EntProtoId` default). `SolreignOrphanReachabilityTest` **excludes C# and comments**. Sibling `SolreignAmbientSpore` is allowlisted for exactly this reason; `SolreignAcidMote` is **not**.

Expect CI fail: `AllNonAbstractSolreignEntities_AreReachable_OrAllowlisted`.

Add to `Resources/_Solreign/orphan_allowlist.yml` (mirror AmbientSpore):

```yaml
- id: SolreignAcidMote
  reason: >-
    Spawned only by SolreignAcidStormRuleComponent.MoteEffectPrototype
    via SolreignAcidStormRule — cosmetic particle, never map/vendor placed.
```

#### Soft / non-blocking

- **20 PointLights:** fine; SporeDrift already accepted 14 preferred.
- **No map `WeatherEventPrototypes` entries for `SolreignAcidStorm`:** preferred path never densifies (always 10). OK for this wave unless you want Oasis/Terminus to lean in.
- **Double-start:** second sting/VO/scatter layers; accepted.
- **Mid-join miss:** accepted for weather.

---

### Checklist (your 7 questions)

1. **Started matches verdict** — yes  
2. **weight 3** — correct “rare / wow” intent  
3. **30s motes** — OK design; fix overclaiming comment  
4. **ForceEndSelf** — blocks Started effects; **does not** block Added start or Ended all-clear  
5. **Tone** — pass; popup slightly edgier  
6. **Component reuse** — fine  
7. **Fresh bugs** — **orphan allowlist must-fix**; optional silent kill-switch polish  

**Commit bar:** add `SolreignAcidMote` to orphan allowlist. Everything else is optional polish / doc honesty.
