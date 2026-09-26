# Grand Strategy (working title)

A modern-day grand strategy game made in **Unity 6.6**. The game starts on 1 January 2026 with every real country, split into provinces.

The special feature is the **Capital Battle**. Most fighting is simple and happens province by province. But when an army reaches an enemy capital, a real-time battle decides that nation's fate. Generals command every division, and you can take direct control of one division or all of them. The full design is in [`docs/DESIGN.md`](docs/DESIGN.md).

## Status: Phase 1, "Run your nation"

The game is built in phases (full roadmap in [`docs/DESIGN.md`](docs/DESIGN.md), section 6):

| Phase | Theme | State |
|---|---|---|
| 1 | Run your nation: economy, politics, diplomacy, events, new interface | **Done** |
| 2 | Military and war, plus win conditions | Next |
| 3 | Living world: AI nations that act on their own, world crises | Planned |
| 4 | Capital Battle in 3D (URP, generals, control one division or all of them) | Planned |
| 5 | Technology, save/load, tutorial, balance and polish | Planned |

What you can do today:

- **Click any country** to open its panel. It has four tabs: **Overview**, **Economy**, **Politics** and **Diplomacy**. Every number has a tooltip explaining where it comes from.
- **Economy.** Each country has GDP, growth, inflation, a budget (taxes, foreign aid, five spending areas and interest), a deficit that turns into debt, an interest rate and a credit rating (AAA to D). Pay more interest than you can afford for six months and you default.
  - For your own country, sliders set the tax rate and the spending on the military, welfare, education, infrastructure and administration. The panel shows the monthly balance and the effect on approval as you drag.
  - You can borrow, repay debt, and launch a stimulus package.
- **Politics.** Each country has a government type (full democracy to absolute monarchy), stability, and public approval.
  - Democracies hold elections, and you can lose them.
  - Low stability brings protests. Stability under 10 for three months brings a revolution.
  - Decisions: propaganda, reforms, crackdowns (not for democracies) and early elections.
- **Diplomacy.** Countries have relations from −100 to +100, set by blocs (EU, NATO, BRICS, ASEAN and more), rivalries and your actions.
  - Actions: improve relations, denounce, trade deals, sanctions, alliances and military pacts.
  - Before you propose something, the panel shows whether the other side will say yes, and why.
  - Countries you sanction may sanction you back.
- **Events with choices.** Protests, election results, debt crises, trade offers, sanctions, scandals, disasters and booms. The game pauses until you choose.
- **A real-world start (2026).** Tax levels, debt, military spending, government types, alliances, rivalries and the sanctions in force are all based on each country's real situation.
- **You can lose.** If your government is overthrown, or your nation is annexed, the game ends.
- **A new look.** Flags, icons, a new font, a top bar with your key numbers, a news feed, and a gold outline on the selected country.
- **Seven map modes:** Political, Diplomatic, Wealth, Growth, Stability, Government and Population.
- Also from Milestone 1:
  - A real-world map of **2,450 provinces** and **198 countries** with real terrain.
  - Smooth borders and country names on the map.
  - Generated music and sound effects.
  - The **Capital Battle rules**: a winner who loses under 65% of its troops annexes the whole country; 65% or more takes only the capital and leaves the loser a desperate last capital. You can test these from the developer panel (**F12**) until armies arrive in Phase 2.

## How to open it

> **Pressing Play shows only a sky, and the Project window says Assets is empty?**
> Unity opened the wrong folder and created a new, empty project there. None of the game is in it.
> The folder you open in Unity Hub must **directly** contain `Assets`, `Packages`, `ProjectSettings` and `README.md`. See step 3 below.

1. Install [Unity Hub](https://unity.com/download). In Unity Hub go to **Installs → Install Editor** and install **Unity 6.6 (6000.6.3f1)** or any newer 6.6 version.
2. Get the project. Either option works; GitHub Desktop is easier.
   - **Recommended: GitHub Desktop.**
     1. Install [GitHub Desktop](https://desktop.github.com/).
     2. Choose **File → Clone repository → URL**, enter `mrguymrguy647-del/Grand-strategy-game`, and pick a short local path such as `C:\Games`.
     3. Use the **Current branch** menu at the top to switch to the branch you were given (for example `claude/practical-davinci-4niemu`).
     4. To get updates later, click **Fetch origin**, then **Pull origin**. Unity then re-imports only what changed.
   - **ZIP.**
     1. On GitHub, pick the branch, then choose **Code → Download ZIP** and unzip it.
     2. Windows unzips it into **a folder inside a folder with the same name**. The project is the **inner** folder.
     3. Move the inner folder to a short path such as `C:\Games\GrandStrategy`.
3. Check the folder before you open it. It must contain `Assets`, `Packages`, `ProjectSettings`, `Tools`, `docs` and `README.md`. If you only see one folder inside, open that folder and look again.
4. In Unity Hub go to **Projects → Add → Add project from disk** and choose that folder. Open it. The first time, Unity takes a few minutes to set it up.
5. If Unity asks whether to **enable the new Input System backends**, click **Yes**. Unity restarts once.
6. Check that you are in the right project:
   - the menu bar has a **Grand Strategy** menu;
   - the Project window's `Assets` shows `Resources`, `Scripts` and `StreamingAssets`;
   - `Packages` lists **Input System**.
7. Press **Play** ▶. A loading screen appears, then the world map. The game starts from any scene.

## If something goes wrong

- **Self-test:** press `F12` and click **Run self-test**. It plays through every screen and action in about 30 seconds, then shows a report. If anything failed, click **Copy report** and send it. Click **New game** afterwards, because the test changes the world.

- A red box appears at the bottom of the game window whenever an error happens. Click **Copy errors** and paste the text to your developer. It includes your Unity version and graphics card.
- **Settings** (top right) shows which map renderer and input system are in use.
- If the menu bar has no **Grand Strategy** item, or nothing responds to the mouse, check Unity's **Console** window for red errors and send those too.

## Controls

| Action | Keys |
|---|---|
| Move the map | `W A S D` / arrow keys, or drag with any mouse button |
| Zoom | Mouse wheel, or `Q` / `E` |
| Select a country (opens its panel) | Left click (right click or `Esc` to close) |
| Pause / resume | `Space` |
| Game speed | `1`–`5`, or `+` / `-` |
| Map modes | `F1` Political, `F2` Diplomatic, `F3` Wealth, `F4` Growth, `F5` Stability, `F6` Government, `F7` Population |
| Explain a number | Hover over it |
| Developer tools (self-test, test events, Capital Battle) | `F12` |
| Mute | `M` |

## Project layout

```
Assets/
  Scripts/Simulation/   Game rules in pure C#, with no Unity code (tested outside Unity)
    Economy/ Politics/ Diplomacy/ Events/   The Phase 1 nation systems
    Core/GameSimulation.cs                  Runs them every in-game month
  Scripts/Game/         Unity side: bootstrap, map, camera, input, UI, audio
  Scripts/Editor/       Editor-only setup (creates the main scene)
  Resources/Shaders/    WorldMap shader (terrain, colours, borders, selection)
  Resources/UI/         Game.uss stylesheet (colours only) and runtime theme
  Resources/Fonts/      Barlow fonts
  StreamingAssets/UI/   Flags/ and Icons/ (PNG files the game decodes itself; see Credits)
  StreamingAssets/Data/ Map/ (provinces, countries), World/ (nations, diplomacy), Rules/ (tuning)
docs/DESIGN.md          Game design
Tests/                  .NET unit tests
Tools/mapgen/           Builds the map data from Natural Earth, and the nation data
Tools/assets/           Downloads and prepares flags, icons and fonts
Tools/unity_meta.py     Creates stable .meta files for files added outside Unity
```

## Tuning

All the numbers are in `Assets/StreamingAssets/Data/`. Edit a file and press Play again; no code changes are needed.

- `Rules/war.json`: the 65% heavy-loss line and the size and length of each morale bonus or penalty.
- `Rules/economy.json`: tax drag, how interest rates react to debt, when a default happens, and more.
- `Rules/politics.json`: how fast approval and stability move, protest and revolution thresholds, and election rules.
- `Rules/diplomacy.json`: how relations drift, and what it takes for the AI to accept a trade deal or alliance.
- `World/nations.json` and `World/diplomacy.json`: each country's starting economy and government, plus blocs, relations and sanctions. These are generated by `Tools/mapgen/generate_nations.py`, so edit that script rather than the JSON.

## Adding your own music and sounds

Drop audio files into `Assets/Resources/Audio/Music/<Mood>/` or `Assets/Resources/Audio/Sfx/` and they replace the generated sounds automatically. See [`Assets/Resources/Audio/README.txt`](Assets/Resources/Audio/README.txt) for the names.

## For developers

Run the tests. This needs the .NET 8 SDK, but not Unity:

```
dotnet test Tests/Simulation.Tests   # clock, world, Capital Battle, economy, politics, diplomacy, a 10-year world run
dotnet test Tests/Audio.Tests        # every music loop and sound effect renders cleanly
dotnet build Tests/UnityCompileCheck # compiles Assets/Scripts like Unity: one assembly per .asmdef, C# 9
```

Rebuild the map data after changing the generators:

```
pip install -r Tools/mapgen/requirements.txt
python3 Tools/mapgen/generate_map.py       # provinces.png, provinces.json, countries.json
python3 Tools/mapgen/generate_terrain.py   # terrain.jpg (relief, water, lakes, rivers)
python3 Tools/mapgen/generate_nations.py   # nations.json, diplomacy.json (economy, government, blocs, sanctions)
pip install cairosvg pillow numpy
python3 Tools/assets/fetch_ui_assets.py    # flags, icons, fonts
python3 Tools/unity_meta.py
```

## Credits

- Map data and terrain relief: [Natural Earth](https://www.naturalearthdata.com/), public domain. The shaded-relief image comes from the `basemap-data` package on PyPI. Borders and country assignments follow Natural Earth's defaults (with Palestine as its own country), and population and GDP figures are its estimates. Province populations are spread out from the country totals.
- Flags: [flag-icons](https://github.com/lipis/flag-icons) by Panayiotis Lipiridis and contributors, MIT License.
- Icons: [game-icons.net](https://game-icons.net) by Lorc and Delapouite, [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/). They were recoloured to white.
- Font: [Barlow](https://github.com/google/fonts/tree/main/ofl/barlowsemicondensed) by Jeremy Tribby, SIL Open Font License 1.1.
- Starting economic and political figures are rounded approximations drawn from public sources (IMF, World Bank, SIPRI, EIU Democracy Index) and simplified for the game.
