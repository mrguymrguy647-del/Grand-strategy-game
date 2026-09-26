# Grand Strategy (working title) — Design Document

Modern-day grand strategy game built in **Unity 6.6 (6000.6.2f1)**.
Start date **1 January 2026**. Every real country, split into provinces.

---

## 1. Pillars

| Pillar | What it means for the player |
|---|---|
| **Economy** | GDP, taxes, budget, debt, trade, resources |
| **Politics** | Government type, stability, approval, elections |
| **Diplomacy** | Relations, alliances, sanctions, trade deals, the UN |
| **War** | Simple province war on the map → **Capital Battle** decides a nation's fate |
| **Modern touches** | Nuclear deterrence, cyber operations, proxy wars |

Time runs in **pausable real time**. One tick = one in-game day.
Speeds 1–5, `Space` pauses.

---

## 2. War

War has two layers.

### 2.1 Province war (strategic layer)

* Armies sit in provinces and move between neighbouring provinces.
* When hostile armies meet, combat is **auto-resolved** with a simple formula
  (manpower, equipment, terrain, general skill, morale).
* The winner takes the province. Fast, readable, no micromanagement.

### 2.2 Capital Battle (the decisive battle)

When an attacking army **reaches the enemy capital province**, the world map
pauses and a **Capital Battle** starts in its own battle scene.

* It is a real-time, **top-down tactical battle** (Total War style camera)
  fought over the capital city and its surroundings.
* Each side brings the divisions that were fighting in and next to the capital.
* **Generals** command every division. Generals are RPG characters:
  level, XP, skills (e.g. *Urban Warfare*, *Aggressive*, *Defensive Genius*),
  traits, and they can die.
* The generals fight the battle on their own.
  The player can **take command of one division, several, or all of them** at
  any moment — and hand them back to their generals whenever they want.
* AI vs AI capital battles are auto-resolved using the same outcome rules.

### 2.3 Outcome rules

`lossRatio = manpower the winner lost in the battle ÷ manpower the winner brought`

| Result | Condition | Effect |
|---|---|---|
| **Total victory** | Attacker wins, `lossRatio < 65%` | Attacker **annexes the whole defending country**. |
| **Pyrrhic victory** | Attacker wins, `lossRatio ≥ 65%` | Attacker takes **only the capital province**. The defender moves its capital to its best remaining province (**Last Capital**) and gets the **Desperate** morale penalty. Reaching the new capital triggers another Capital Battle. |
| **Capital held** | Defender wins | Attacking army is routed. Defender gets the **Heroic Defense** morale bonus. The war continues. |

* The same rules apply both ways: if the AI beats the player cleanly the
  player's nation is annexed (**game over**); a costly AI win means the player
  fights on from their Last Capital.
* A country that loses its last province is eliminated.
* All numbers live in `Assets/StreamingAssets/Data/Rules/war.json`
  (`heavyLossThreshold = 0.65`, morale modifiers and their durations) so they
  can be tuned without touching code.

---

## 3. Audio

Audio is a first-class system, not an afterthought.

* **Music** is picked by *mood* and cross-fades when the mood changes:
  `Menu`, `Peace`, `Tension`, `War`, `CapitalBattle`, `Victory`, `Defeat`.
* **Sound effects** for UI (clicks, speed changes, pause), map (province
  select), time (new month / new year), and war events (war declared,
  capital battle start, victory, defeat).
* Every sound and track has a **procedurally synthesized placeholder**, so the
  game has full audio from day one without any asset files.
* Real audio drops in with no code changes: put files in
  `Assets/Resources/Audio/Music/<Mood>/` or `Assets/Resources/Audio/Sfx/<name>`
  and they replace the placeholder (see `Assets/Resources/Audio/README.txt`).
* Music and SFX volume are adjustable in the in-game settings and saved.

---

## 4. Technical architecture

```
Assets/
  Scripts/
    Simulation/   Pure C# game rules. No UnityEngine reference (enforced by asmdef).
                  Unit-tested outside Unity (Tests/Simulation.Tests).
    Game/         Unity layer: bootstrap, map rendering, camera, input, UI, audio.
  Resources/      UI theme, optional audio overrides.
  StreamingAssets/Data/
    Map/          provinces.png (province ID map), provinces.json, countries.json
    Rules/        war.json and other tunable numbers
Tools/mapgen/     Python tool that builds the map data from Natural Earth
Tests/            .NET test project for the simulation
```

* **Simulation / presentation split.** The simulation owns all state and rules;
  Unity only draws it and forwards player input. This keeps the rules testable,
  makes save/load and AI easier, and lets the Capital Battle scene be a separate
  module that receives a *battle setup* and returns a *battle result*.
* **Province ID map.** Every province has a unique colour in
  `provinces.png`. The game reads it once, builds a lookup table
  (pixel → province), and renders map modes by recolouring.
* **Map data** is generated from [Natural Earth](https://www.naturalearthdata.com/)
  (public domain) admin-1 boundaries, projected with the Miller projection.
  Small subdivisions are merged into their regions for playability; micro-states
  too small to show on the map are left out for now.
* **UI** is UI Toolkit, built in code.
* **No hand-made scenes needed yet.** `GameBootstrap` builds the game when you
  press Play in any scene.

---

## 5. Milestones

1. **World map** — real-world provinces, pan/zoom, click provinces, nation
   selection, date + speed controls, map modes, music + SFX, Capital Battle
   outcome rules (with developer test buttons until armies exist). ✅ *built, awaiting first play test*
2. **Countries & economy** — budget, taxes, GDP growth, debt.
3. **Diplomacy** — relations, alliances, sanctions, declaring war.
4. **Province war** — armies, movement, auto-resolved province combat, occupation.
5. **Capital Battle** — tactical battle scene, generals, take-command system,
   outcome rules wired to the world map.
6. **Politics** — stability, elections, government collapse.
7. **AI** — other nations plan, trade, ally and go to war.
8. **Events, technology, save/load, polish.**
