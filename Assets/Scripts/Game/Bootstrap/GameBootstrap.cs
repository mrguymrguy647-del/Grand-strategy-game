using System.Globalization;
using UnityEngine;

namespace GrandStrategy.Game
{
    /// <summary>
    /// Starts the game when you press Play in any scene: no hand-made scene or prefab is needed.
    /// If a scene already contains a <see cref="GameRoot"/>, that one is used instead.
    /// </summary>
    public static class GameBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            // Numbers are shown as "45.5%" whatever the player's Windows region is set to
            // (and the game's fonts don't have every region's digit and separator glyphs).
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            ErrorConsole.Ensure();
            if (Object.FindAnyObjectByType<GameRoot>() != null)
                return;
            new GameObject("Game").AddComponent<GameRoot>();
        }
    }
}
