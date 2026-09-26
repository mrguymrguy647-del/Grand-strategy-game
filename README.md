# Grand Strategy (working title)

A modern-day grand strategy game made in **Unity 6.6**. The game starts on 1 January 2026 with every real country, split into provinces.

The special feature is the **Capital Battle**. Most fighting is simple and happens province by province. But when an army reaches an enemy capital, a real-time battle decides that nation's fate. Generals command every division, and you can take direct control of one division or all of them. The full design is in [`docs/DESIGN.md`](docs/DESIGN.md).

## Status: Milestone 1, the world map

- A real-world map: **2,454 provinces** and **198 countries** built from Natural Earth borders.
- Pan and zoom around the map, hover over and click provinces.
- Pick any country and **play as it**.
- A clock with **pause and 5 speeds**, starting 1 January 2026.
- Map modes: **Political**, **Population** and **Wealth**.
- Capital markers on the map. Your capital is gold, and a desperate "last capital" is red.
- **Music and sound effects.** Every mood (menu, peace, tension, war, capital battle, victory, defeat) has its own music track, and every UI action and event has a sound. All of it is generated in code, so no audio files are needed.
- **Capital Battle rules** are implemented and tested:
  - A winner who loses **less than 65%** of their troops annexes the whole country.
  - A winner who loses **65% or more** takes only the capital. The loser sets up a desperate last capital and gets a morale penalty.
  - If the defender wins, the capital holds.

  Until armies exist, the country panel has test buttons that apply these rules, so you can watch them work on the map.

## How to open it

1. Install [Unity Hub](https://unity.com/download).
2. In Unity Hub go to **Installs → Install Editor** and install **Unity 6.6 (6000.6.2f1)** or any newer 6.6 version.
3. Clone this repository, or download it as a ZIP and unzip it.
4. In Unity Hub go to **Projects → Add → Add project from disk** and choose the repository folder.
5. Open the project. The first time, Unity takes a few minutes to set it up.
6. Press **Play** ▶. You don't need a special scene; the game builds itself in whatever scene is open.

If Unity asks whether to enable the new Input System backends, either answer works: the game supports both.

## Controls

| Action | Keys |
|---|---|
| Move the map | `W A S D` / arrow keys, or drag with any mouse button |
| Zoom | Mouse wheel, or `Q` / `E` |
| Select a province | Left click (right click or `Esc` to deselect) |
| Pause / resume | `Space` |
| Game speed | `1`–`5`, or `+` / `-` |
| Map modes | `F1` Political, `F2` Population, `F3` Wealth |
| Mute | `M` |

## Project layout

```
Assets/
  Scripts/Simulation/   Game rules in pure C#, with no Unity code (tested outside Unity)
  Scripts/Game/         Unity side: bootstrap, map, camera, input, UI, audio
  StreamingAssets/Data/ Map data (provinces.png / .json, countries.json) and rules (war.json)
  Resources/            UI theme, plus optional audio files to replace the generated ones
docs/DESIGN.md          Game design
Tests/                  .NET unit tests
Tools/mapgen/           Builds the map data from Natural Earth
Tools/unity_meta.py     Creates stable .meta files for files added outside Unity
```

## Tuning

The war numbers are in `Assets/StreamingAssets/Data/Rules/war.json`. They include the 65% heavy-loss line and the size and length of each morale bonus or penalty. Edit the file and press Play again; no code changes are needed.

## Adding your own music and sounds

Drop audio files into `Assets/Resources/Audio/Music/<Mood>/` or `Assets/Resources/Audio/Sfx/` and they replace the generated sounds automatically. See [`Assets/Resources/Audio/README.txt`](Assets/Resources/Audio/README.txt) for the names.

## For developers

Run the tests. This needs the .NET 8 SDK, but not Unity:

```
dotnet test Tests/Simulation.Tests   # dates, clock, world, Capital Battle rules, map data checks
dotnet test Tests/Audio.Tests        # every music loop and sound effect renders cleanly
```

Rebuild the map data after changing the generator:

```
pip install -r Tools/mapgen/requirements.txt
python3 Tools/mapgen/generate_map.py
python3 Tools/unity_meta.py
```

## Credits

- Map data: [Natural Earth](https://www.naturalearthdata.com/), public domain. Borders and country assignments follow Natural Earth's defaults (with Palestine as its own country), and population and GDP figures are its estimates. Province populations are spread out from the country totals.
