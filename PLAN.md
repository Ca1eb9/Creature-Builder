# Creature Builder — Ship Plan

Goal: deliver a polished, working Windows build to Mom **once**, with no
follow-up patch needed. Plan created 2026-07-06.

## Decisions (locked in)

- Target platform: **Windows** (her machine confirmed)
- Scope: **all 8 categories including Horns & Accessories**
- companyName: **Caleb Penley** (set 2026-07-06 — FROZEN, see below)
- Delivery: USB or cloud link — final note to Mom will cover SmartScreen either way

## ⚠️ Frozen before ship — never change after Mom has a save file

| Frozen | Why |
|---|---|
| `BodyPartCategory` enum order | Saves store category as int; reordering scrambles slots. Append only. |
| Every `partID` | Saves reference parts by ID; regenerating orphans saved creatures. |
| `companyName` + `productName` | Determine persistentDataPath; renaming "loses" all her saves. |
| Save folder name (`Creatures`) + JSON field names | Same reason. |
| Object names inside each model FBX (`Head`, `Torso`, ...) | `Reimport Selected Model` re-attaches saved part IDs by matching model name + category. Rename an object and the match fails, so the part gets a fresh ID and orphans every save using it. |
| Model FBX filenames (`Wasp.fbx`) | Same matching rule — the filename is half the key. |

**Post-ship model repairs:** prefer a *geometry-only* fix (holes, normals,
materials) — overwrite the FBX and the generated prefabs pick up the new mesh
automatically, with IDs and calibration untouched. Only an origin / rotation /
scale change needs `Reimport Selected Model`, which preserves IDs and
calibration but *only* while the names above are unchanged.

Ownership legend — **[ME]**: Claude, unsupervised. **[ME+V]**: Claude does it,
Caleb visually verifies in Unity. **[YOU]**: Caleb only.

## Phase 0 — Unblock save/load

- [x] 0.1 [ME] Migrate 4 BodyPartData assets to new schema (partID, scaleMultiplier, useAutoScale) — done 2026-07-06
- [x] 0.2 [ME] Validator editor script: `Tools > Creature Builder > Validate Parts` + `Fix Missing Part IDs` — done 2026-07-06
- [x] 0.3 [YOU] Play mode: save → clear → load round-trip — verified 2026-07-06

## Phase 1 — Repair the UI shell (2–4 hrs)

- [x] 1.1 [ME+V] Panel layout fixer (Tools > Creature Builder > Fix Panel Layouts) — verified 2026-07-06; also rebuilt LoadListEntry (root was a TMP text object with no Button, so loading was never wired)
- [x] 1.2 [ME+V] AttachPoint_Horns / AttachPoint_Accessories added + wired — positions still need tuning with real models
- [x] 1.3 [ME+V] Info panel created bottom-left + wired (hides when nothing selected) — done 2026-07-06, quick visual check pending
- [x] 1.6 [ME+V] PartButton "Icon" Image child added; UIManager toggles it by sprite presence — done 2026-07-06
- [x] 1.4 [YOU] Visual taste pass — approved 2026-07-06 (aspect-ratio check also fine)
- [x] 1.5 [ME] Delete TutorialInfo/, root screenshots — done 2026-07-06

## Phase 2 — Real content (8–15 hrs, the long pole)

- [ ] 2.1 [YOU] Scope: aim ~3–4 parts × 8 categories; Horns/Accessories can have fewer
- [ ] 2.2 [YOU] Download CC0 packs (Quaternius, Kenney, Poly Pizza — guide §5)
- [ ] 2.3 [YOU] Split models in Blender (separate → set origin → export FBX; ~1–2 hrs/animal at first)
- [x] 2.4 [ME] Batch importer: `Tools > Creature Builder > Import Parts Folder...` (auto BodyPartData + partID + database + rendered icons) — done 2026-07-06, batch self-test passing
- [ ] 2.5 [YOU] Calibrate positionOffset/scaleMultiplier per part (safe to refine post-ship)

## Phase 3 — Mom-proofing (3–5 hrs)

- [x] 3.1 [ME] Save toast + overwrite warning — done 2026-07-06 (needs visual check)
- [x] 3.2 [ME] Delete confirmation dialog — done 2026-07-06 (needs visual check)
- [x] 3.3 [ME] Screenshot feedback toast — done 2026-07-06 (needs visual check)
- [x] 3.4 [ME] Startup creature (randomize on launch) — done 2026-07-06 (needs visual check)
- [x] 3.5 [ME] Input field char limit (30) — done 2026-07-06
- [x] 3.6 [ME] Graceful-degradation: null guards in UIManager + CreatureSaveLoad; init-order races fixed (CameraFramer & PartAdjustmentPanel now init in Awake) — done 2026-07-06
- [ ] 3.7 [YOU] Full 9-step smoke test (architecture doc §10F) in editor

## Phase 4 — Build & deliver (2–3 hrs)

- [ ] 4.1 [ME+V] Player settings: window title, icon, resolution defaults
- [ ] 4.2 [YOU] Build Windows x64
- [ ] 4.3 [YOU] Smoke test in the BUILT exe (saves + screenshots especially)
- [ ] 4.4 [ME] "Read me first" note for Mom (includes SmartScreen "More info → Run anyway" walkthrough); [YOU] zip excluding `*_BurstDebugInformation_DoNotShip`
- [ ] 4.5 [YOU] Deliver (USB avoids SmartScreen; cloud link triggers it — note covers both)
- [ ] 4.6 [YOU] Ideally test zip on a second Windows machine/account first

## Estimates

| Phase | Claude | Caleb |
|---|---|---|
| 0 | 30 min | 10 min |
| 1 | 1–2 hrs | 1–2 hrs |
| 2 | 1 hr | 8–15 hrs |
| 3 | 2–3 hrs | 1–2 hrs |
| 4 | 30 min | 2 hrs |

Content (Phase 2) dominates. Everything else is a few evenings.
