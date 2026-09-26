using System.IO;
using System.Linq;
using GrandStrategy.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GrandStrategy.EditorTools
{
    /// <summary>
    /// First-time project setup, run once per editor session:
    /// creates Assets/Scenes/Main.unity (camera + game), adds it to the build, and opens it when
    /// Unity starts on an empty untitled scene. Pressing Play then starts the game.
    /// (The game also starts from any other scene, so this is a convenience, not a requirement.)
    /// </summary>
    [InitializeOnLoad]
    static class ProjectSetup
    {
        public const string MainScenePath = "Assets/Scenes/Main.unity";
        const string SessionKey = "GrandStrategy.ProjectSetup.Ran";

        static ProjectSetup()
        {
            if (SessionState.GetBool(SessionKey, false))
                return;
            SessionState.SetBool(SessionKey, true);
            EditorApplication.delayCall += Run;
        }

        [MenuItem("Grand Strategy/Open Main Scene")]
        static void OpenMainScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            if (!File.Exists(MainScenePath))
                CreateMainScene();
            else
                EditorSceneManager.OpenScene(MainScenePath);
            EnsureBuildSettings();
        }

        static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            var active = SceneManager.GetActiveScene();
            bool emptyUntitled = SceneManager.sceneCount == 1 && string.IsNullOrEmpty(active.path) && !active.isDirty;
            if (!emptyUntitled)
            {
                if (File.Exists(MainScenePath))
                    EnsureBuildSettings();
                return;
            }

            if (File.Exists(MainScenePath))
                EditorSceneManager.OpenScene(MainScenePath);
            else
                CreateMainScene();
            EnsureBuildSettings();
        }

        /// <summary>Replaces the current (empty) scene with a new main scene and saves it.</summary>
        static void CreateMainScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MainScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 10f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(14, 30, 48, 255);
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            cameraObject.AddComponent<AudioListener>();

            new GameObject("Game").AddComponent<GameRoot>();

            EditorSceneManager.SaveScene(scene, MainScenePath);
            Debug.Log($"Grand Strategy: created {MainScenePath}. Press Play to start the game.");
        }

        static void EnsureBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes;
            if (scenes.Any(s => s.path == MainScenePath))
                return;
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MainScenePath, true) }.Concat(scenes).ToArray();
        }
    }
}
