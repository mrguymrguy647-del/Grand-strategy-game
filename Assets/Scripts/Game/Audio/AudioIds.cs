namespace GrandStrategy.Game.Audio
{
    /// <summary>Background music moods. Changing mood cross-fades to that mood's music.</summary>
    public enum MusicMood
    {
        None,
        Menu,
        Peace,
        Tension,
        War,
        CapitalBattle,
        Victory,
        Defeat,
    }

    /// <summary>Every sound effect in the game.</summary>
    public enum Sfx
    {
        UiClick,
        UiHover,
        ProvinceSelect,
        SpeedUp,
        SpeedDown,
        Pause,
        Resume,
        NewMonth,
        NewYear,
        NationChosen,
        Notification,
        WarDeclared,
        CapitalBattleStart,
        Victory,
        Defeat,
    }

    public static class AudioIds
    {
        /// <summary>
        /// File name used for a sound effect override in Resources/Audio/Sfx, e.g. "ui_click".
        /// </summary>
        public static string FileName(Sfx sfx)
        {
            var name = sfx.ToString();
            var sb = new System.Text.StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]))
                    sb.Append('_');
                sb.Append(char.ToLowerInvariant(name[i]));
            }
            return sb.ToString();
        }
    }
}
