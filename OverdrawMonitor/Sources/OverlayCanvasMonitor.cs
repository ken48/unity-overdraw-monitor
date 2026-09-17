using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace ken48.Overdraw
{
    // Counts Screen Space - Overlay UI once with an internal camera independent of
    // user cameras. Other Canvas render modes are intentionally unsupported.
    public class OverlayCanvasMonitor : BaseMonitor
    {
        struct CanvasState
        {
            public Canvas canvas;
            public RenderMode renderMode;
            public Camera worldCamera;
            public float planeDistance;
        }

        const float CameraPositionZ = -100000f;

        public override string displayName => "All Overlay Canvases";

        readonly List<Canvas> _targets = new();
        readonly List<CanvasState> _canvasStates = new();
        Camera _debugCamera;
        float _canvasPlaneDistance;

        public void SetRendererIndex(int rendererIndex)
        {
            if (_debugCamera == null)
            {
                return;
            }

            _debugCamera
               .GetUniversalAdditionalCameraData()
               .SetRenderer(rendererIndex);
        }

        public void SetTargetCanvases(Canvas[] canvases)
        {
            _targets.Clear();

            foreach (Canvas canvas in canvases)
            {
                if (!canvas.isRootCanvas
                 || canvas.targetDisplay != 0)
                {
                    continue;
                }

                _targets.Add(canvas);
            }
        }

        protected override bool InitializeMonitor()
        {
            var cameraObject = new GameObject("UI Overdraw Debug Camera");
            cameraObject.hideFlags = HideFlags.HideAndDontSave;
            cameraObject.transform.SetParent(transform, worldPositionStays: false);

            cameraObject.transform.SetPositionAndRotation(
                new Vector3(x: 0f, y: 0f, CameraPositionZ),
                Quaternion.identity
            );

            _debugCamera = cameraObject.AddComponent<Camera>();
            _debugCamera.enabled = false;
            _debugCamera.orthographic = true;
            _debugCamera.nearClipPlane = 0.1f;
            _debugCamera.farClipPlane = 2f;
            _debugCamera.clearFlags = CameraClearFlags.SolidColor;
            _debugCamera.backgroundColor = Color.clear;
            _debugCamera.allowHDR = false;
            _debugCamera.allowMSAA = false;
            _debugCamera.allowDynamicResolution = false;

            return true;
        }

        protected override bool TryGetRenderResolution(out Vector2Int resolution)
        {
            bool hasActiveCanvas = false;

            foreach (Canvas canvas in _targets)
            {
                if (canvas != null
                 && canvas.isActiveAndEnabled
                 && canvas.targetDisplay == 0
                 && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    hasActiveCanvas = true;
                    break;
                }
            }

            resolution = new Vector2Int(Screen.width, Screen.height);
            return hasActiveCanvas;
        }

        protected override void RenderOverdraw(RenderTexture target)
        {
            _debugCamera.targetTexture = target;
            _debugCamera.rect = new Rect(x: 0f, y: 0f, width: 1f, height: 1f);
            _debugCamera.aspect = target.width / (float)target.height;
            _debugCamera.orthographicSize = target.height * 0.5f;
            _canvasPlaneDistance = Mathf.Max(target.width, target.height) * 2f;
            _debugCamera.farClipPlane = _canvasPlaneDistance * 2f;

            try
            {
                // Preparation mutates live Canvas state, so it must be covered by
                // the same finally block as rendering.
                PrepareCanvases();
                _debugCamera.Render();
            }
            finally
            {
                RestoreCanvases();
            }
        }

        protected override void DisposeMonitor()
        {
            try
            {
                RestoreCanvases();
            }
            finally
            {
                _targets.Clear();

                if (_debugCamera != null)
                {
                    _debugCamera.targetTexture = null;
                    DestroyRuntimeObject(_debugCamera.gameObject);
                    _debugCamera = null;
                }
            }
        }

        void PrepareCanvases()
        {
            RestoreCanvases();

            foreach (Canvas canvas in _targets)
            {
                if (canvas == null
                 || !canvas.isActiveAndEnabled
                 || canvas.targetDisplay != 0
                 || canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                {
                    continue;
                }

                _canvasStates.Add(
                    new CanvasState
                    {
                        canvas = canvas,
                        renderMode = canvas.renderMode,
                        worldCamera = canvas.worldCamera,
                        planeDistance = canvas.planeDistance
                    }
                );

                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = _debugCamera;
                canvas.planeDistance = _canvasPlaneDistance;
            }

            // Rebuild after changing render modes so the debug camera receives the
            // current UI geometry, including objects created during this frame.
            Canvas.ForceUpdateCanvases();
        }

        void RestoreCanvases()
        {
            foreach (CanvasState state in _canvasStates)
            {
                if (state.canvas == null)
                {
                    continue;
                }

                state.canvas.renderMode = state.renderMode;
                state.canvas.worldCamera = state.worldCamera;
                state.canvas.planeDistance = state.planeDistance;
            }

            bool restoredCanvases = _canvasStates.Count > 0;
            _canvasStates.Clear();

            if (restoredCanvases)
            {
                Canvas.ForceUpdateCanvases();
            }
        }
    }
}
