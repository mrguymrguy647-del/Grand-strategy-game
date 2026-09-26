using UnityEngine;
#if GS_INPUT_SYSTEM_PACKAGE && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace GrandStrategy.Game
{
    public enum GameKey
    {
        Pause,
        Speed1,
        Speed2,
        Speed3,
        Speed4,
        Speed5,
        SpeedUp,
        SpeedDown,
        PanUp,
        PanDown,
        PanLeft,
        PanRight,
        ZoomIn,
        ZoomOut,
        Cancel,
        ToggleMute,
        MapMode1,
        MapMode2,
        MapMode3,
    }

    public enum PointerButton
    {
        Left = 0,
        Right = 1,
        Middle = 2,
    }

    /// <summary>
    /// One place for all keyboard/mouse reads. Works with the new Input System package when it is
    /// installed and enabled, and with the classic Input Manager otherwise.
    /// </summary>
    public static class GameInput
    {
#if GS_INPUT_SYSTEM_PACKAGE && ENABLE_INPUT_SYSTEM
        static Key[] KeysFor(GameKey key)
        {
            switch (key)
            {
                case GameKey.Pause: return new[] { Key.Space };
                case GameKey.Speed1: return new[] { Key.Digit1, Key.Numpad1 };
                case GameKey.Speed2: return new[] { Key.Digit2, Key.Numpad2 };
                case GameKey.Speed3: return new[] { Key.Digit3, Key.Numpad3 };
                case GameKey.Speed4: return new[] { Key.Digit4, Key.Numpad4 };
                case GameKey.Speed5: return new[] { Key.Digit5, Key.Numpad5 };
                case GameKey.SpeedUp: return new[] { Key.Equals, Key.NumpadPlus };
                case GameKey.SpeedDown: return new[] { Key.Minus, Key.NumpadMinus };
                case GameKey.PanUp: return new[] { Key.W, Key.UpArrow };
                case GameKey.PanDown: return new[] { Key.S, Key.DownArrow };
                case GameKey.PanLeft: return new[] { Key.A, Key.LeftArrow };
                case GameKey.PanRight: return new[] { Key.D, Key.RightArrow };
                case GameKey.ZoomIn: return new[] { Key.E, Key.PageUp };
                case GameKey.ZoomOut: return new[] { Key.Q, Key.PageDown };
                case GameKey.Cancel: return new[] { Key.Escape };
                case GameKey.ToggleMute: return new[] { Key.M };
                case GameKey.MapMode1: return new[] { Key.F1 };
                case GameKey.MapMode2: return new[] { Key.F2 };
                case GameKey.MapMode3: return new[] { Key.F3 };
                default: return System.Array.Empty<Key>();
            }
        }

        static readonly Key[][] KeyTable = BuildKeyTable();

        static Key[][] BuildKeyTable()
        {
            var values = (GameKey[])System.Enum.GetValues(typeof(GameKey));
            var table = new Key[values.Length][];
            foreach (var v in values)
                table[(int)v] = KeysFor(v);
            return table;
        }

        public static bool KeyDown(GameKey key)
        {
            var kb = Keyboard.current;
            if (kb == null) return false;
            foreach (var k in KeyTable[(int)key])
                if (kb[k].wasPressedThisFrame) return true;
            return false;
        }

        public static bool KeyHeld(GameKey key)
        {
            var kb = Keyboard.current;
            if (kb == null) return false;
            foreach (var k in KeyTable[(int)key])
                if (kb[k].isPressed) return true;
            return false;
        }

        public static bool HasPointer => Mouse.current != null;

        public static Vector2 PointerPosition => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;

        static UnityEngine.InputSystem.Controls.ButtonControl Button(PointerButton b)
        {
            var m = Mouse.current;
            if (m == null) return null;
            switch (b)
            {
                case PointerButton.Right: return m.rightButton;
                case PointerButton.Middle: return m.middleButton;
                default: return m.leftButton;
            }
        }

        public static bool PointerDown(PointerButton b) => Button(b)?.wasPressedThisFrame ?? false;
        public static bool PointerUp(PointerButton b) => Button(b)?.wasReleasedThisFrame ?? false;
        public static bool PointerHeld(PointerButton b) => Button(b)?.isPressed ?? false;

        /// <summary>Scroll wheel movement this frame, roughly +1 per notch away from the user.</summary>
        public static float ScrollSteps
        {
            get
            {
                var m = Mouse.current;
                if (m == null) return 0;
                float y = m.scroll.ReadValue().y;
                // Some platforms report 120 per notch, others 1.
                return Mathf.Abs(y) > 10f ? y / 120f : y;
            }
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        static KeyCode[] KeysFor(GameKey key)
        {
            switch (key)
            {
                case GameKey.Pause: return new[] { KeyCode.Space };
                case GameKey.Speed1: return new[] { KeyCode.Alpha1, KeyCode.Keypad1 };
                case GameKey.Speed2: return new[] { KeyCode.Alpha2, KeyCode.Keypad2 };
                case GameKey.Speed3: return new[] { KeyCode.Alpha3, KeyCode.Keypad3 };
                case GameKey.Speed4: return new[] { KeyCode.Alpha4, KeyCode.Keypad4 };
                case GameKey.Speed5: return new[] { KeyCode.Alpha5, KeyCode.Keypad5 };
                case GameKey.SpeedUp: return new[] { KeyCode.Equals, KeyCode.Plus, KeyCode.KeypadPlus };
                case GameKey.SpeedDown: return new[] { KeyCode.Minus, KeyCode.KeypadMinus };
                case GameKey.PanUp: return new[] { KeyCode.W, KeyCode.UpArrow };
                case GameKey.PanDown: return new[] { KeyCode.S, KeyCode.DownArrow };
                case GameKey.PanLeft: return new[] { KeyCode.A, KeyCode.LeftArrow };
                case GameKey.PanRight: return new[] { KeyCode.D, KeyCode.RightArrow };
                case GameKey.ZoomIn: return new[] { KeyCode.E, KeyCode.PageUp };
                case GameKey.ZoomOut: return new[] { KeyCode.Q, KeyCode.PageDown };
                case GameKey.Cancel: return new[] { KeyCode.Escape };
                case GameKey.ToggleMute: return new[] { KeyCode.M };
                case GameKey.MapMode1: return new[] { KeyCode.F1 };
                case GameKey.MapMode2: return new[] { KeyCode.F2 };
                case GameKey.MapMode3: return new[] { KeyCode.F3 };
                default: return System.Array.Empty<KeyCode>();
            }
        }

        static readonly KeyCode[][] KeyTable = BuildKeyTable();

        static KeyCode[][] BuildKeyTable()
        {
            var values = (GameKey[])System.Enum.GetValues(typeof(GameKey));
            var table = new KeyCode[values.Length][];
            foreach (var v in values)
                table[(int)v] = KeysFor(v);
            return table;
        }

        public static bool KeyDown(GameKey key)
        {
            foreach (var k in KeyTable[(int)key])
                if (Input.GetKeyDown(k)) return true;
            return false;
        }

        public static bool KeyHeld(GameKey key)
        {
            foreach (var k in KeyTable[(int)key])
                if (Input.GetKey(k)) return true;
            return false;
        }

        public static bool HasPointer => Input.mousePresent;
        public static Vector2 PointerPosition => Input.mousePosition;
        public static bool PointerDown(PointerButton b) => Input.GetMouseButtonDown((int)b);
        public static bool PointerUp(PointerButton b) => Input.GetMouseButtonUp((int)b);
        public static bool PointerHeld(PointerButton b) => Input.GetMouseButton((int)b);
        public static float ScrollSteps => Input.mouseScrollDelta.y;
#else
        // No input backend is enabled (Project Settings > Player > Active Input Handling).
        static bool _warned;

        static bool Warn()
        {
            if (!_warned)
            {
                _warned = true;
                Debug.LogError("No input backend enabled. Set Project Settings > Player > Active Input Handling to 'Both' " +
                               "or install the Input System package.");
            }
            return false;
        }

        public static bool KeyDown(GameKey key) => Warn();
        public static bool KeyHeld(GameKey key) => Warn();
        public static bool HasPointer => Warn();
        public static Vector2 PointerPosition => Vector2.zero;
        public static bool PointerDown(PointerButton b) => Warn();
        public static bool PointerUp(PointerButton b) => Warn();
        public static bool PointerHeld(PointerButton b) => Warn();
        public static float ScrollSteps => 0;
#endif
    }
}
