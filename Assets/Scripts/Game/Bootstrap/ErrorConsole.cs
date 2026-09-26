using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace GrandStrategy.Game
{
    /// <summary>
    /// Shows errors on screen while playing, with a button that copies them to the clipboard,
    /// so problems can be reported without digging through Unity's Console window.
    /// Drawn with IMGUI so it keeps working even if the game's own UI fails.
    /// </summary>
    public sealed class ErrorConsole : MonoBehaviour
    {
        const int MaxEntries = 30;

        static ErrorConsole _instance;

        readonly List<string> _entries = new List<string>();
        Rect _drawn; // screen area of the box, in GUI coordinates (y down)
        int _count;
        bool _collapsed;
        Vector2 _scroll;
        GUIStyle _text;
        GUIStyle _title;

        public static void Ensure()
        {
            if (FindAnyObjectByType<ErrorConsole>() != null)
                return;
            var go = new GameObject("Error Console");
            DontDestroyOnLoad(go);
            go.AddComponent<ErrorConsole>();
        }

        void OnEnable()
        {
            _instance = this;
            Application.logMessageReceived += OnLog;
        }

        void OnDisable()
        {
            Application.logMessageReceived -= OnLog;
            if (_instance == this)
                _instance = null;
        }

        /// <summary>True if the error box is drawn at this screen position (y up), so clicks there don't reach the map.</summary>
        public static bool Covers(Vector2 screenPosition)
        {
            if (_instance == null || _instance._count == 0)
                return false;
            return _instance._drawn.Contains(new Vector2(screenPosition.x, Screen.height - screenPosition.y));
        }

        void OnLog(string message, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
                return;
            _count++;
            _entries.Add($"[{type}] {message}\n{stackTrace}".TrimEnd());
            if (_entries.Count > MaxEntries)
                _entries.RemoveAt(0);
            _collapsed = false;
        }

        void OnGUI()
        {
            if (_count == 0)
                return;
            GUI.depth = -1000;
            _text ??= new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 13, richText = false };
            _title ??= new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, wordWrap = true };

            if (_collapsed)
            {
                _drawn = new Rect(12, Screen.height - 44, 190, 32);
                if (GUI.Button(_drawn, $"Show errors ({_count})"))
                    _collapsed = false;
                return;
            }

            float width = Mathf.Min(820, Screen.width - 24);
            float height = Mathf.Min(300, Screen.height * 0.45f);
            var rect = new Rect((Screen.width - width) / 2f, Screen.height - height - 12, width, height);
            _drawn = rect;
            GUI.color = new Color(1f, 0.55f, 0.5f, 1f);
            GUI.Box(rect, GUIContent.none);
            GUI.Box(rect, GUIContent.none);
            GUI.color = Color.white;

            GUILayout.BeginArea(new Rect(rect.x + 10, rect.y + 8, rect.width - 20, rect.height - 16));
            GUILayout.Label($"Something went wrong ({_count} error{(_count == 1 ? "" : "s")}). " +
                            "Press \"Copy errors\" and paste them to your developer.", _title);
            _scroll = GUILayout.BeginScrollView(_scroll);
            for (int i = _entries.Count - 1; i >= 0 && i >= _entries.Count - 5; i--)
                GUILayout.Label(Shorten(_entries[i]), _text);
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy errors", GUILayout.Height(28)))
                GUIUtility.systemCopyBuffer = FullReport();
            if (GUILayout.Button("Hide", GUILayout.Height(28), GUILayout.Width(90)))
                _collapsed = true;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        static string Shorten(string entry)
        {
            var lines = entry.Split('\n');
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Length && i < 5; i++)
                sb.AppendLine(lines[i]);
            return sb.ToString().TrimEnd();
        }

        string FullReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Unity {Application.unityVersion}, {SystemInfo.operatingSystem}, {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType})");
            sb.AppendLine($"Input: {GameInput.BackendName}");
            sb.AppendLine();
            foreach (var e in _entries)
            {
                sb.AppendLine(e);
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
