# Grand Strategy (working title) — Design Document

Modern-day grand strategy game built in **Unity 6.6 (6000.6.3f1 or newer)**.
Start date **1 January 2026**. Every real country, split into provinces.

---

## 1. Pillars

| Pillar | What it means for the player |
|---|---|
| **Economy** | GDP, taxes, budget, debt, trade (Phase 1 ✅), resources (later) |
| **Politics** | Government type, stability, approval, elections (Phase 1 ✅) |
| **Diplomacy** | Relations, alliances, sanctions, trade deals (Phase 1 ✅), the UN (later) |
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

## 3. Running a nation (Phase 1)

Every system is monthly and deterministic for a given seed. They run in a fixed order:
economy → politics → diplomacy → events (`GameSimulation.RunMonth`). AI countries use the
same rules as the player. Every derived number keeps a **breakdown** (a list of labelled
factors), so the UI can explain it in a tooltip, and the AI can explain its answer to a proposal.
All tuning lives in `Data/Rules/economy.json`, `politics.json` and `diplomacy.json`.

### 3.1 Starting data

`Tools/mapgen/generate_nations.py` writes `Data/World/nations.json` and `Data/World/diplomacy.json`:

* **Nations.** For each of the 198 countries it writes:
  * Government type, tax revenue, debt, interest rate, safe debt level and foreign aid.
  * The five spending shares, real growth, inflation and population growth.
  * Starting stability and approval, and the election calendar.
* **Where the values come from.** About 60 major countries are entered by hand. The rest get defaults from their World Bank income group.
* **Diplomacy.** 19 blocs (alliances, unions, trade areas and forums: NATO, CSTO, EU, BRICS, ASEAN, GCC and others), 99 relation overrides (rivalries and friendships), and the sanction regimes in force in 2026.

### 3.2 Economy

* **GDP** lives in the provinces (so the Wealth map follows it). Each month it grows by the real growth rate plus inflation.
* **Growth** = economic potential + tax level + infrastructure investment + education + stability + debt burden + trade agreements − sanctions against us − cost of our own sanctions ± modifiers ± a small business cycle.
  * Policy effects are measured against each country's **own starting policy**, so the world is in balance on day one and only changes move the needle.
* **Budget.**
  * Revenue: GDP × tax rate × collection efficiency (weaker if administration is underfunded), plus foreign aid.
  * Costs: military, welfare, education, infrastructure and administration (each a share of GDP), plus interest.
  * A deficit is borrowed and becomes debt.
* **Interest rate** moves slowly (debt rolls over), towards the base rate plus a risk premium. The premium grows with debt above the country's safe level and with instability. The result sets a **credit rating** from AAA to D.
* **Default.** If interest costs more than 55% of revenue for 6 months, the country defaults:
  * 30% of its debt is written off;
  * growth drops sharply, and borrowing becomes expensive;
  * the player chooses between an IMF programme and going it alone.
* **Player actions:** tax rate (5–60%), each spending share (0–35%), borrow, repay, stimulus package.
* **AI** adjusts its taxes and spending each January to keep deficits in check.

### 3.3 Politics

* **Government types:** full democracy, flawed democracy, hybrid regime, authoritarian, absolute monarchy.
* **Approval** moves towards a target made of: the starting baseline, economic growth, taxes, welfare, education and modifiers.
* **Stability** moves towards a target made of: the baseline, approval, **public anger** (very low approval), security forces (worth more to autocracies), administration and modifiers.
* **Elections** happen on each country's real calendar, for democracies and hybrid regimes. The chance of re-election depends on approval. Losing brings in a new government with a honeymoon modifier.
* **Protests** can start below stability 35. **Revolution** follows after 3 months below 10:
  * for AI countries, the regime changes;
  * for the player, **the game is lost**.
* **Decisions** (with cooldowns): stimulus package, propaganda campaign, reform programme, crackdown (not for democracies), early election (democracies only).

### 3.4 Diplomacy

* **Relations** run from −100 to +100. They drift slowly towards a target made of:
  * shared blocs (alliance > union > trade area > forum);
  * similar or clashing regimes;
  * trade deals and sanctions;
  * historical overrides (rivalries and friendships);
  * **goodwill** from recent actions, which decays over time.
* **Actions:**
  * Improve relations (costs money, has a cooldown), denounce.
  * Propose or cancel a trade deal.
  * Impose or lift sanctions.
  * Propose a military pact, join or leave an alliance.
  * *Declare war* is shown but locked until Phase 2.
* **Consent.** Proposals are scored by the other side. The panel shows the verdict and the reasons **before** you click, and the answer popup lists them again.
* **Effects.** Trade deals add growth to both sides, scaled by the partner's economy. Sanctions cut the target's growth by the combined economic weight of the sanctioners, and cost the sanctioner a little.
* **AI initiative.**
  * AI countries offer the player trade deals.
  * Hostile pairs sometimes sanction each other.
  * Countries you sanction may retaliate.

### 3.5 Events

* National events pause the game and offer up to three choices, each with its effects spelled out:
  * protests (negotiate, crack down, wait);
  * election results;
  * debt default;
  * trade offers;
  * sanctions received;
  * random events: scandal, natural disaster, tech boom, energy shock, general strike, foreign investment.
* A news feed reports what happens to you and to the big economies (at least 1% of world GDP).

### 3.6 Win and lose

* **Lose:** your government is overthrown (revolution), or your nation is annexed.
* **Win:** arrives with Phase 2 (war), because the win conditions are military and economic dominance.

---

## 4. Audio

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

## 5. Technical architecture

```
Assets/
  Scripts/
    Simulation/   Pure C# game rules. No UnityEngine reference (enforced by asmdef).
                  Unit-tested outside Unity (Tests/Simulation.Tests).
      Core/       GameSimulation (monthly tick), Breakdown/Factor, NationSetup
      Economy/ Politics/ Diplomacy/ Events/
    Game/         Unity layer: bootstrap, map rendering, camera, input, UI, audio.
  Resources/      UI stylesheet (UI/Game.uss, colours only), fonts, optional audio overrides.
  StreamingAssets/UI/  Flags and icons as plain PNGs, decoded by the game at runtime.
  StreamingAssets/Data/
    Map/          provinces.png (province ID map), provinces.json, countries.json
    World/        nations.json, diplomacy.json (starting economy, politics, blocs)
    Rules/        war.json, economy.json, politics.json, diplomacy.json
Tools/mapgen/     Python tools that build the map and nation data
Tools/assets/     Downloads and prepares flags, icons and fonts
Tests/            .NET test project for the simulation
```

* **Simulation / presentation split.** The simulation owns all state and rules;
  Unity only draws it and forwards player input. This keeps the rules testable,
  makes save/load and AI easier, and lets the Capital Battle scene be a separate
  module that receives a *battle setup* and returns a *battle result*.
* **Province ID map.** Every province has a unique colour in
  `provinces.png`. The game reads it once into a pixel → province table
  (used for clicks) and a 16-bit id texture for the GPU.
* **Map rendering.** The `GrandStrategy/WorldMap` shader combines three textures:
  the baked terrain (`terrain.jpg`: shaded relief, water depth, lakes, rivers), the
  province id texture, and a tiny per-province colour table. Borders come from comparing
  province ids over a 4x4 texel footprint with a tent filter, which gives smooth,
  anti-aliased lines at a constant on-screen width. Changing ownership or map mode only
  rewrites the colour table. A CPU renderer is kept as a fallback for old GPUs.
* **Country names** are UI Toolkit labels placed on each country's largest connected
  area, rotated along its main axis and sized to fit (principal-component analysis of
  the province pixels), so they follow conquests automatically.
* **Map data** is generated from [Natural Earth](https://www.naturalearthdata.com/)
  (public domain) admin-1 boundaries, projected with the Miller projection.
  Small subdivisions are merged into their regions for playability; micro-states
  too small to show on the map are left out for now.
* **Selection.** Clicking selects a whole country. The shader brightens it and draws a gold outline, using the owner code stored in the colour table's alpha channel. The player's country gets a thin gold outline.
* **Map modes** (F1–F7): Political, Diplomatic (relations with you, allies, sanctions), Wealth, Growth, Stability, Government and Population. All of them only rewrite the colour table.
* **UI** is UI Toolkit, built in code and styled by `Resources/UI/Game.uss`.
  * Views: `TopBar`, `CountryPanel` (tabs), `EventPopup`, `NotificationLog`, `TooltipManager`, and screens for loading, settings, game over and the F12 developer panel.
  * Hover tooltips are custom, because UI Toolkit shows none at runtime.
  * **Images don't go through Unity's texture importer.** Flags and icons are PNGs in `StreamingAssets/UI`, decoded with `Texture2D.LoadImage` (as the map is). Panel gradients and the vignette are generated in code. The stylesheet references no images. This keeps the interface identical on every machine, whatever the import settings.
  * **Self-test (F12).** `SelfTest.cs` plays through every screen and action: all tabs, all map modes, every tooltip, economy changes, decisions, diplomacy, events, 24 months of time and the Capital Battle rules. It reports failed checks and every error logged along the way.
* **No hand-made scenes needed yet.** `GameBootstrap` builds the game when you
  press Play in any scene.

---

## 6. Roadmap

The game is built in phases, starting with the systems that every later phase uses: money, stability and relations.

| Phase | Theme | Contents |
|---|---|---|
| 0 | World map ✅ | Real-world provinces, pan and zoom, nation selection, clock, map modes, music and sound effects, Capital Battle outcome rules (with developer test buttons). |
| **1** | **Run your nation ✅** | Economy, politics, diplomacy, events and decisions, the new interface, the country panel, more map modes. You lose if your government is overthrown. |
| 2 | Military & war | Armies bought from the budget and moved on the map. Declaring war pulls in allies. Province combat, occupation, war score, peace deals and war exhaustion. Auto-resolved Capital Battles use the 65% rule. **Win conditions** and a victory screen. |
| 3 | Living world | AI nations run their economies, form alliances, sanction and fight each other. World crises: recessions, oil shocks, pandemics. |
| 4 | Capital Battle (tactical) | Move to URP. A 3D battle at the capital with RPG generals. The player can control one division or all of them. 3D models made in Blender. |
| 5 | Depth & polish | Technology, save/load, more events, a tutorial, balance and performance. |
