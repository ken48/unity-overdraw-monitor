using System;
using UnityEngine;

namespace ken48.Overdraw
{
    // Owns the render target and GPU reduction shared by world-camera and UI monitors.
    public abstract class BaseMonitor : MonoBehaviour
    {
        const int GroupDimension = 32;
        static readonly int FragmentWeightId = Shader.PropertyToID("OverdrawFragmentWeight");
        static readonly int BufferSizeXId = Shader.PropertyToID("BufferSizeX");
        static readonly int OverdrawId = Shader.PropertyToID("Overdraw");
        static readonly int OutputId = Shader.PropertyToID("Output");

        public long fragmentsCount =>
            isActiveAndEnabled
                ? _fragmentsCount
                : 0L;

        public long screenPixelCount =>
            isActiveAndEnabled
                ? _screenPixelCount
                : 0L;

        public Vector2Int screenResolution =>
            isActiveAndEnabled
                ? _screenResolution
                : Vector2Int.zero;

        public Vector2Int renderResolution =>
            isActiveAndEnabled
                ? _renderResolution
                : Vector2Int.zero;

        public abstract string displayName { get; }

        public bool isReady => isActiveAndEnabled && !_isShutDown;

        RenderTexture _overdrawTexture;
        ComputeShader _computeShader;
        ComputeBuffer _resultBuffer;
        int[] _resultData;
        int _kernel;
        int _xGroups;
        int _yGroups;
        long _fragmentsCount;
        long _screenPixelCount;
        Vector2Int _screenResolution;
        Vector2Int _renderResolution;
        bool _isShutDown;

        protected virtual void Awake()
        {
            try
            {
                _computeShader = Resources.Load<ComputeShader>("OverdrawParallelReduction");

                if (!SystemInfo.supportsComputeShaders
                 || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RFloat)
                 || _computeShader == null)
                {
                    Debug.LogError(
                        "Overdraw Monitor requires compute shaders, RFloat render targets and "
                      + "OverdrawParallelReduction.compute.",
                        this
                    );

                    DisableAndShutDown();
                    return;
                }

                _kernel = _computeShader.FindKernel("CSMain");

                if (!InitializeMonitor())
                {
                    DisableAndShutDown();
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                DisableAndShutDown();
            }
        }

        protected virtual void LateUpdate()
        {
            try
            {
                int screenWidth = Screen.width;
                int screenHeight = Screen.height;

                if (screenWidth <= 0
                 || screenHeight <= 0
                 || !TryGetRenderResolution(out Vector2Int resolution)
                 || resolution.x <= 0
                 || resolution.y <= 0)
                {
                    ResetResult();
                    return;
                }

                EnsureRenderResources(resolution.x, resolution.y);
                ClearOverdrawTexture();

                // 1 / 1024 is exactly representable in RFloat. The compute shader
                // multiplies each pixel back before performing its integer reduction.
                float previousFragmentWeight = Shader.GetGlobalFloat(FragmentWeightId);
                Shader.SetGlobalFloat(FragmentWeightId, 1f / (GroupDimension * GroupDimension));

                try
                {
                    RenderOverdraw(_overdrawTexture);
                }
                finally
                {
                    Shader.SetGlobalFloat(FragmentWeightId, previousFragmentWeight);
                }

                _computeShader.SetInt(BufferSizeXId, _xGroups);
                _computeShader.SetTexture(_kernel, OverdrawId, _overdrawTexture);
                _computeShader.SetBuffer(_kernel, OutputId, _resultBuffer);
                _computeShader.Dispatch(_kernel, _xGroups, _yGroups, threadGroupsZ: 1);

                // A synchronous readback is intentional: this is an Editor diagnostic,
                // and the displayed value must belong to the frame rendered above.
                _resultBuffer.GetData(_resultData);
                _fragmentsCount = 0L;

                foreach (int result in _resultData)
                {
                    _fragmentsCount += result;
                }

                _screenPixelCount = (long)screenWidth * screenHeight;
                _screenResolution = new Vector2Int(screenWidth, screenHeight);
                _renderResolution = resolution;
            }
            catch (Exception exception)
            {
                ResetResult();
                Debug.LogException(exception, this);
                DisableAndShutDown();
            }
        }

        protected virtual void OnDestroy()
        {
            ShutDown();
        }

        protected abstract bool InitializeMonitor();
        protected abstract bool TryGetRenderResolution(out Vector2Int resolution);
        protected abstract void RenderOverdraw(RenderTexture target);
        protected abstract void DisposeMonitor();

        protected void ResetResult()
        {
            _fragmentsCount = 0L;
            _screenPixelCount = 0L;
            _screenResolution = Vector2Int.zero;
            _renderResolution = Vector2Int.zero;
        }

        void EnsureRenderResources(int width, int height)
        {
            if (_overdrawTexture != null
             && _overdrawTexture.width == width
             && _overdrawTexture.height == height)
            {
                return;
            }

            ReleaseRenderResources();

            _overdrawTexture = new RenderTexture(width, height, depth: 24, RenderTextureFormat.RFloat)
            {
                name = "Overdraw Counts",
                hideFlags = HideFlags.HideAndDontSave
            };

            if (!_overdrawTexture.Create())
            {
                throw new InvalidOperationException(
                    $"Could not create the {width}x{height} RFloat overdraw render target."
                );
            }

            _xGroups = (width + GroupDimension - 1) / GroupDimension;
            _yGroups = (height + GroupDimension - 1) / GroupDimension;
            _resultData = new int[_xGroups * _yGroups];
            _resultBuffer = new ComputeBuffer(_resultData.Length, sizeof(int));
        }

        void ClearOverdrawTexture()
        {
            RenderTexture previousActive = RenderTexture.active;

            try
            {
                RenderTexture.active = _overdrawTexture;
                GL.Clear(clearDepth: true, clearColor: true, Color.clear);
            }
            finally
            {
                RenderTexture.active = previousActive;
            }
        }

        void DisableAndShutDown()
        {
            enabled = false;
            ShutDown();
        }

        void ShutDown()
        {
            if (_isShutDown)
            {
                return;
            }

            _isShutDown = true;

            try
            {
                DisposeMonitor();
            }
            finally
            {
                ReleaseRenderResources();
            }
        }

        void ReleaseRenderResources()
        {
            _resultBuffer?.Release();
            _resultBuffer = null;
            _resultData = null;

            if (_overdrawTexture == null)
            {
                return;
            }

            _overdrawTexture.Release();
            DestroyRuntimeObject(_overdrawTexture);
            _overdrawTexture = null;
        }

        protected static void DestroyRuntimeObject(UnityEngine.Object value)
        {
            if (value == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(value);
            }
            else
            {
                DestroyImmediate(value);
            }
        }
    }
}
