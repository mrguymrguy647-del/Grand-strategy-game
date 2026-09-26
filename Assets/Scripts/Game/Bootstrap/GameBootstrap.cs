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
            if (Object.FindAnyObjectByType<GameRoot>() != null)
                return;
            new GameObject("Game").AddComponent<GameRoot>();
        }
    }
}
