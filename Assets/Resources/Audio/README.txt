REPLACING THE GAME'S MUSIC AND SOUND EFFECTS
============================================

Every sound in the game is synthesized in code, so the game has full audio with no files.
To use real audio instead, drop files (.wav, .ogg or .mp3) into these folders.
Nothing else is needed: the game picks them up automatically.

MUSIC  ->  Assets/Resources/Audio/Music/<Mood>/
  Put one or more tracks in a mood folder. Several tracks play as a shuffled playlist.
  Moods:
    Menu           nation selection screen
    Peace          world map in peacetime
    Tension        crises and alerts
    War            while at war
    CapitalBattle  during a Capital Battle
    Victory        after a great victory
    Defeat         when your nation falls

SOUND EFFECTS  ->  Assets/Resources/Audio/Sfx/<name>
  The file name (without extension) picks the sound it replaces:
    ui_click, ui_hover, province_select, speed_up, speed_down, pause, resume,
    new_month, new_year, nation_chosen, notification, war_declared,
    capital_battle_start, victory, defeat

Only use audio you have the rights to (for example CC0 / public domain, or your own).
