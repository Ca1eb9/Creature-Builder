# Shipping Creature Builder

The repeatable procedure for cutting a release. Follow it top to bottom every
time — the checklist exists so a release never depends on remembering.

Target: **Windows 10/11 x64**, delivered to a non-technical user.

---

## 0. Know where the app touches the machine

Two locations, both writable without admin rights. Nothing is installed, no
registry keys, no admin prompt.

| What | Where | Notes |
|---|---|---|
| Saved creatures + thumbnails | `%USERPROFILE%\AppData\LocalLow\Caleb Penley\Creature Builder\Creatures\` | Created on first save. One `.json` + one `.png` per creature. |
| Screenshots | `%USERPROFILE%\Downloads\Creature_<date>.png` | Folder is created if missing. |

**This is why `companyName` and `productName` are frozen** — they *are* the save
path. Renaming either makes every existing creature vanish from her library
(the files remain on disk under the old name, but the app looks elsewhere).

---

## 1. Pre-ship checklist

### Content
- [ ] `Tools > Creature Builder > Validate Parts` → **0 errors**
- [ ] `Tools > Creature Builder > Clean Up Database` → no empty entries, no
      part missing a prefab
- [ ] Test/placeholder parts removed (`Delete Selected Parts`)
- [ ] Every category has at least one part, or is deliberately empty
- [ ] Old test saves deleted from the Library (they may reference deleted parts)

### Settings (Edit > Project Settings > Player)
- [ ] `Company Name` = **Caleb Penley** — frozen
- [ ] `Product Name` = **Creature Builder** — frozen
- [ ] `Version` bumped (see *Versioning* below)
- [ ] Default window 1600×900, resizable
- [ ] Icon set (Player > Icon)

### Smoke test in the Editor
- [ ] Launch → a random creature appears, no console errors
- [ ] Click through every category; equip and un-equip ("No head") parts
- [ ] Search "badger" → results from multiple categories
- [ ] Adjust every slider; press **Reset** — transforms reset, selection kept
- [ ] Collapse and re-open both panels
- [ ] Save → toast, thumbnail appears on the library card, no UI in the picture
- [ ] Load it back → identical creature
- [ ] Save again under the same name → overwrite confirm appears
- [ ] Delete → confirm dialog; card disappears
- [ ] Screenshot → lands in Downloads, no UI or tooltip in the image
- [ ] Exit → confirm dialog, app closes

### Save compatibility (only from v1.0.1 onward)
- [ ] Copy a `Creatures` folder from the previous version into the save path and
      confirm every creature still loads with all parts
- [ ] No part IDs changed — see the frozen list in `PLAN.md`

---

## 2. Build

1. **File > Build Profiles** → Platform **Windows**, Architecture **x64**
2. Ensure `Assets/Scenes/MainScene` is the only scene, and it is ticked
3. **Build** → into `Builds/CreatureBuilder-v<version>/`
4. Confirm the output contains:
   - `Creature Builder.exe`
   - `Creature Builder_Data/`
   - `UnityPlayer.dll`, `MonoBleedingEdge/`

**Delete before zipping:** any `*_BurstDebugInformation_DoNotShip` folder.
It is debug symbols, adds tens of MB, and ships nothing useful.

**Include with the zip:** `LICENSE` and `THIRD-PARTY-NOTICES.md`. The fonts are
under the SIL Open Font License, which requires its text to travel with them —
the OFL files live in `Assets/Fonts/` and are carried into the build, but
shipping the notices at the top level makes the licensing legible.

### Test the built exe (not the Editor)
The Editor hides real problems. Run the actual build and repeat the smoke test,
paying attention to:
- [ ] Saving and loading (the save path only resolves correctly in a build)
- [ ] Screenshot writing to a real Downloads folder
- [ ] Window launches at a sensible size on a 1080p display

---

## 3. Signing — and why we're not

Windows SmartScreen warns on any executable without an established reputation.
Removing that warning requires an **Authenticode code-signing certificate**
(~$200–400/year, OV; EV certs clear SmartScreen immediately, others need to
build reputation over many downloads). A self-signed certificate does **not**
help — SmartScreen ignores it.

**Decision: ship unsigned.** For a one-recipient family app the cost is not
justified. Instead, avoid the warning at the source:

| Delivery | SmartScreen? | Why |
|---|---|---|
| **USB stick** | **No** | Files copied locally carry no Mark of the Web. |
| Download (GitHub/Drive) | Yes | Windows tags downloaded files; the tag propagates to everything extracted from the zip. |

If she downloads it, the tag can be cleared **before** extracting:
right-click the `.zip` → **Properties** → tick **Unblock** → OK → then extract.
That single step removes the warning for every file inside.

---

## 4. Delivery

### First release → USB
Simplest, no warnings, and you can start it once for her. Copy the whole
unzipped folder to her Desktop and make a shortcut to the `.exe`.

### Later releases → GitHub Releases
Gives a stable link and keeps every version.

1. `git tag v<version> && git push origin v<version>`
2. GitHub → **Releases** → **Draft a new release** → pick the tag
3. Attach `CreatureBuilder-v<version>.zip`
4. Paste the "what's new" notes
5. Send her the release page link

> ⚠️ The repo is public, so release assets are public too. Fine for this app —
> just don't attach anything private.

**Always include the note in section 6** with a downloaded build.

---

## 5. Versioning

`Version` in Player Settings, semver:

- **Patch** (1.0.**1**) — bug fixes only, no content changes
- **Minor** (1.**1**.0) — new parts/models added (safe: new parts get new IDs)
- **Major** (**2**.0.0) — anything that breaks her saves. Avoid; if unavoidable,
  tell her explicitly that old creatures won't load.

Tag the commit you built from, so a bug report maps to exact code:
`git tag v1.0.0 && git push origin v1.0.0`

---

## 6. The note to send with it

> **Creature Builder**
>
> To start: open the folder and double-click **Creature Builder**.
>
> If Windows shows a blue "Windows protected your PC" box, that only means the
> app isn't from the Microsoft Store. Click **More info**, then **Run anyway**.
> (It'll only ask the first time.)
>
> - Drag on the creature to spin it, scroll to zoom
> - Pick parts from the left, adjust them on the right
> - **Save creature** keeps it in your Library
> - **Screenshot** puts a picture in your Downloads folder
>
> Your creatures are saved on your own computer — nothing goes online.
>
> Love, Caleb

---

## 7. After shipping

- [ ] Tag pushed, and the tagged commit is the one you built
- [ ] A copy of the exact zip you sent, kept locally
- [ ] `PLAN.md` frozen-invariants list still accurate

**Never after shipping:** rename model FBX files or the objects inside them,
reorder `BodyPartCategory`, regenerate part IDs, or change `companyName` /
`productName`. Each one silently breaks creatures she has already made.
