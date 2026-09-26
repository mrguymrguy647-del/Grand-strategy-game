using System;
using UnityEngine;

namespace GrandStrategy.Game.Map
{
    /// <summary>
    /// Orthographic strategy camera: WASD/arrows to pan, drag with any mouse button to pan,
    /// scroll (or Q/E) to zoom towards the cursor. Also tells clicks apart from drags.
    /// </summary>
    [DefaultExecutionOrder(-50)] // runs before GameRoot reads the click flags
    public sealed class MapCameraController : MonoBehaviour
    {
        const float DragThresholdPixels = 6f;
        const float MinOrthoSize = 0.6f;
        const float ZoomStep = 0.85f;
        const float KeyboardPanSpeed = 1.1f; // view heights per second
        const float KeyboardZoomSpeed = 2.5f;

        Camera _camera;
        Rect _mapRect;
        Func<Vector2, bool> _isPointerOverUi;

        readonly bool[] _pressed = new bool[3];
        readonly bool[] _pressStartedOverUi = new bool[3];
        readonly Vector2[] _pressStart = new Vector2[3];
        bool _dragging;
        Vector3 _dragAnchorWorld;

        Vector3? _flyTarget;
        float _flySize;

        public Camera Camera => _camera;

        /// <summary>True while the user is dragging the map.</summary>
        public bool IsDragging => _dragging;

        /// <summary>A left click (press + release without dragging) happened this frame on the map.</summary>
        public bool LeftClicked { get; private set; }

        /// <summary>A right click (press + release without dragging) happened this frame on the map.</summary>
        public bool RightClicked { get; private set; }

        public void Initialize(Camera cam, Rect mapRect, Func<Vector2, bool> isPointerOverUi)
        {
            _camera = cam;
            _mapRect = mapRect;
            _isPointerOverUi = isPointerOverUi ?? (_ => false);
            _camera.orthographic = true;
            ShowWholeMap();
        }

        float MaxOrthoSize => Mathf.Max(_mapRect.height / 2f, _mapRect.width / 2f / Mathf.Max(0.1f, _camera.aspect)) * 1.02f;

        public void ShowWholeMap()
        {
            _flyTarget = null;
            _camera.orthographicSize = Mathf.Min(MaxOrthoSize, _mapRect.height / 2f * 1.02f);
            SetPosition(_mapRect.center);
        }

        /// <summary>Smoothly moves the camera to a point, optionally zooming.</summary>
        public void FlyTo(Vector3 worldPosition, float orthoSize)
        {
            _flyTarget = new Vector3(worldPosition.x, worldPosition.y, 0f);
            _flySize = Mathf.Clamp(orthoSize, MinOrthoSize, MaxOrthoSize);
        }

        public Vector3 PointerWorld => ScreenToWorld(GameInput.PointerPosition);

        Vector3 ScreenToWorld(Vector2 screen)
        {
            var p = _camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -_camera.transform.position.z));
            p.z = 0f;
            return p;
        }

        void Update()
        {
            if (_camera == null)
                return;

            LeftClicked = false;
            RightClicked = false;
            float dt = Time.unscaledDeltaTime;
            var pointer = GameInput.PointerPosition;
            bool overUi = GameInput.HasPointer && _isPointerOverUi(pointer);

            HandleButtons(pointer, overUi);
            HandleZoom(pointer, overUi, dt);
            HandleKeyboardPan(dt);
            HandleFlight(dt);
            Clamp();
        }

        void HandleButtons(Vector2 pointer, bool overUi)
        {
            for (int b = 0; b < 3; b++)
            {
                var button = (PointerButton)b;
                if (GameInput.PointerDown(button))
                {
                    _pressed[b] = true;
                    _pressStartedOverUi[b] = overUi;
                    _pressStart[b] = pointer;
                }

                if (_pressed[b] && !_pressStartedOverUi[b] && !_dragging &&
                    (pointer - _pressStart[b]).sqrMagnitude > DragThresholdPixels * DragThresholdPixels)
                {
                    _dragging = true;
                    _flyTarget = null;
                    _dragAnchorWorld = ScreenToWorld(_pressStart[b]);
                }

                if (GameInput.PointerUp(button) && _pressed[b])
                {
                    bool wasClick = !_dragging && !_pressStartedOverUi[b] && !overUi;
                    _pressed[b] = false;
                    if (wasClick && button == PointerButton.Left) LeftClicked = true;
                    if (wasClick && button == PointerButton.Right) RightClicked = true;
                }
            }

            if (_dragging)
            {
                if (!_pressed[0] && !_pressed[1] && !_pressed[2])
                {
                    _dragging = false;
                }
                else
                {
                    // Keep the world point that was grabbed under the cursor.
                    var delta = _dragAnchorWorld - ScreenToWorld(pointer);
                    _camera.transform.position += new Vector3(delta.x, delta.y, 0f);
                }
            }
        }

        void HandleZoom(Vector2 pointer, bool overUi, float dt)
        {
            float steps = overUi ? 0f : GameInput.ScrollSteps;
            if (GameInput.KeyHeld(GameKey.ZoomIn)) steps += KeyboardZoomSpeed * dt;
            if (GameInput.KeyHeld(GameKey.ZoomOut)) steps -= KeyboardZoomSpeed * dt;
            if (Mathf.Abs(steps) < 1e-4f)
                return;

            _flyTarget = null;
            var anchorScreen = GameInput.HasPointer && !overUi ? pointer : new Vector2(Screen.width / 2f, Screen.height / 2f);
            var before = ScreenToWorld(anchorScreen);
            _camera.orthographicSize = Mathf.Clamp(_camera.orthographicSize * Mathf.Pow(ZoomStep, steps), MinOrthoSize, MaxOrthoSize);
            var after = ScreenToWorld(anchorScreen);
            _camera.transform.position += new Vector3(before.x - after.x, before.y - after.y, 0f);
            if (_dragging)
                _dragAnchorWorld += before - after;
        }

        void HandleKeyboardPan(float dt)
        {
            var dir = Vector2.zero;
            if (GameInput.KeyHeld(GameKey.PanUp)) dir.y += 1;
            if (GameInput.KeyHeld(GameKey.PanDown)) dir.y -= 1;
            if (GameInput.KeyHeld(GameKey.PanLeft)) dir.x -= 1;
            if (GameInput.KeyHeld(GameKey.PanRight)) dir.x += 1;
            if (dir == Vector2.zero)
                return;
            _flyTarget = null;
            var move = dir.normalized * (_camera.orthographicSize * 2f * KeyboardPanSpeed * dt);
            _camera.transform.position += new Vector3(move.x, move.y, 0f);
        }

        void HandleFlight(float dt)
        {
            if (_flyTarget == null)
                return;
            float t = 1f - Mathf.Exp(-6f * dt);
            var pos = _camera.transform.position;
            var target = new Vector3(_flyTarget.Value.x, _flyTarget.Value.y, pos.z);
            _camera.transform.position = Vector3.Lerp(pos, target, t);
            _camera.orthographicSize = Mathf.Lerp(_camera.orthographicSize, _flySize, t);
            if ((target - _camera.transform.position).sqrMagnitude < 1e-6f && Mathf.Abs(_camera.orthographicSize - _flySize) < 1e-3f)
                _flyTarget = null;
        }

        void SetPosition(Vector2 xy)
        {
            var z = _camera.transform.position.z;
            _camera.transform.position = new Vector3(xy.x, xy.y, z < -0.01f ? z : -10f);
        }

        void Clamp()
        {
            _camera.orthographicSize = Mathf.Clamp(_camera.orthographicSize, MinOrthoSize, MaxOrthoSize);
            float halfH = _camera.orthographicSize;
            float halfW = halfH * _camera.aspect;
            var p = _camera.transform.position;
            p.x = halfW * 2f >= _mapRect.width ? _mapRect.center.x : Mathf.Clamp(p.x, _mapRect.xMin + halfW, _mapRect.xMax - halfW);
            p.y = halfH * 2f >= _mapRect.height ? _mapRect.center.y : Mathf.Clamp(p.y, _mapRect.yMin + halfH, _mapRect.yMax - halfH);
            _camera.transform.position = p;
        }
    }
}
