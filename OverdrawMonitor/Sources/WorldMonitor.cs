using UnityEngine;
using UnityEngine.Rendering.Universal;

// Counts geometry rendered by one user camera. Overlay UI is handled independently
// because it does not participate in camera rendering until assigned to a camera.

namespace ken48.Overdraw
{
    public class WorldMonitor : BaseMonitor
    {
        public Camera targetCamera => _targetCamera;

        public override string displayName =>
            _targetCamera != null
                ? _targetCamera.name
                : "Camera";

        Camera _targetCamera;
        Camera _debugCamera;

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

        public void SetTargetCamera(Camera camera)
        {
            _targetCamera = camera;
            ResetResult();
        }

        protected override bool InitializeMonitor()
        {
            var debugCameraObject = new GameObject("Overdraw Debug Camera");
            debugCameraObject.hideFlags = HideFlags.HideAndDontSave;
            debugCameraObject.transform.SetParent(transform, worldPositionStays: false);
            _debugCamera = debugCameraObject.AddComponent<Camera>();
            _debugCamera.enabled = false;

            return true;
        }

        protected override bool TryGetRenderResolution(out Vector2Int resolution)
        {
            if (_targetCamera == null)
            {
                resolution = Vector2Int.zero;
                return false;
            }

            resolution = new Vector2Int(_targetCamera.pixelWidth, _targetCamera.pixelHeight);
            return true;
        }

        protected override void RenderOverdraw(RenderTexture target)
        {
            // Settings such as FOV, culling mask and projection can change during play.
            _debugCamera.CopyFrom(_targetCamera);
            _debugCamera.enabled = false;
            _debugCamera.targetTexture = target;
            _debugCamera.rect = new Rect(x: 0f, y: 0f, width: 1f, height: 1f);
            _debugCamera.aspect = _targetCamera.aspect;
            _debugCamera.projectionMatrix = _targetCamera.projectionMatrix;
            _debugCamera.clearFlags = CameraClearFlags.SolidColor;
            _debugCamera.backgroundColor = Color.clear;
            _debugCamera.allowMSAA = false;
            _debugCamera.allowDynamicResolution = false;

            _debugCamera.transform.SetPositionAndRotation(
                _targetCamera.transform.position,
                _targetCamera.transform.rotation
            );

            _debugCamera.worldToCameraMatrix = _targetCamera.worldToCameraMatrix;
            _debugCamera.Render();
        }

        protected override void DisposeMonitor()
        {
            if (_debugCamera == null)
            {
                return;
            }

            _debugCamera.targetTexture = null;
            DestroyRuntimeObject(_debugCamera.gameObject);
            _debugCamera = null;
        }
    }
}
