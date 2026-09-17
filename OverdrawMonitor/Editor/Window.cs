using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ken48.Overdraw.Editor
{
    public class Window : EditorWindow
    {
        class OverdrawStats
        {
            public float maxOverdrawRatio;
        }

        const string RendererAssetPath = "Packages/ken48.overdraw-monitor/Settings/OverdrawRenderer.asset";

        bool isEnabled => _monitorsGo != null && Application.isPlaying;
        GameObject _monitorsGo;
        Dictionary<BaseMonitor, OverdrawStats> _stats;
        float _maxTotalOverdrawRatio;
        bool _overlayCanvasesDirty;
        int _rendererIndex = -1;

        void OnDisable()
        {
            EditorApplication.hierarchyChanged -= MarkOverlayCanvasesDirty;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.quitting -= TryShutdown;
            AssemblyReloadEvents.beforeAssemblyReload -= TryShutdown;
            TryShutdown();
        }

        void OnEnable()
        {
            EditorApplication.hierarchyChanged -= MarkOverlayCanvasesDirty;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.quitting -= TryShutdown;
            AssemblyReloadEvents.beforeAssemblyReload -= TryShutdown;

            EditorApplication.hierarchyChanged += MarkOverlayCanvasesDirty;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.quitting += TryShutdown;
            AssemblyReloadEvents.beforeAssemblyReload += TryShutdown;
        }

        void OnGUI()
        {
            bool rendererReady = TryGetRendererSetup(
                out _,
                out UniversalRenderPipelineAsset pipelineAsset,
                out ScriptableRendererData rendererData,
                out string setupMessage
            );

            if (!rendererReady)
            {
                EditorGUILayout.HelpBox(setupMessage, MessageType.Warning);

                using (new EditorGUI.DisabledScope(
                    Application.isPlaying || pipelineAsset == null || rendererData == null
                ))
                {
                    if (GUILayout.Button("Add Overdraw Renderer to Active URP Asset"))
                    {
                        AddRendererToPipeline(pipelineAsset, rendererData);
                    }
                }

                GUILayout.Space(5);
            }

            if (Application.isPlaying)
            {
                int startButtonHeight = 25;

                using (new EditorGUI.DisabledScope(!rendererReady && !isEnabled))
                {
                    if (GUILayout.Button(
                        isEnabled
                            ? "Stop"
                            : "Start",
                        GUILayout.MaxWidth(100),
                        GUILayout.MaxHeight(startButtonHeight)
                    ))
                    {
                        if (!isEnabled)
                        {
                            try
                            {
                                Init();
                            }
                            catch (Exception exception)
                            {
                                Debug.LogException(exception);
                                TryShutdown();
                            }
                        }
                        else
                        {
                            TryShutdown();
                        }
                    }
                }

                if (!isEnabled)
                {
                    return;
                }

                BaseMonitor[] monitors = GetAllMonitors();

                GUILayout.Space(-startButtonHeight);

                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("Reset Stats", GUILayout.Width(100), GUILayout.Height(20)))
                    {
                        ResetStats();
                    }
                }

                GUILayout.Space(5);

                Vector2Int screenResolution = monitors
                   .Select(monitor => monitor.screenResolution)
                   .FirstOrDefault(resolution => resolution.x > 0 && resolution.y > 0);

                GUILayout.Label($"Screen {screenResolution.x}x{screenResolution.y}");

                GUILayout.Space(5);

                foreach (BaseMonitor monitor in _stats.Keys.ToArray())
                {
                    if (!Array.Exists(monitors, m => m == monitor))
                    {
                        _stats.Remove(monitor);
                    }
                }

                float totalOverdrawRatio = 0f;

                foreach (BaseMonitor monitor in monitors)
                {
                    using (new GUILayout.HorizontalScope())
                    {
                        Vector2Int resolution = monitor.renderResolution;
                        GUILayout.Label($"{monitor.displayName} {resolution.x}x{resolution.y}");

                        GUILayout.FlexibleSpace();

                        long screenPixelCount = monitor.screenPixelCount;

                        float overdrawRatio = screenPixelCount > 0
                            ? monitor.fragmentsCount / (float)screenPixelCount
                            : 0f;

                        totalOverdrawRatio += overdrawRatio;

                        if (!_stats.TryGetValue(monitor, out OverdrawStats monitorStats))
                        {
                            monitorStats = new OverdrawStats();
                            _stats.Add(monitor, monitorStats);
                        }

                        monitorStats.maxOverdrawRatio = Math.Max(overdrawRatio, monitorStats.maxOverdrawRatio);

                        GUILayout.Label(
                            FormatResult("Overdraw: {0}  (max {1})", overdrawRatio, monitorStats.maxOverdrawRatio)
                        );
                    }
                }

                GUILayout.Space(5);

                _maxTotalOverdrawRatio = Math.Max(_maxTotalOverdrawRatio, totalOverdrawRatio);

                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.Label("Total");
                    GUILayout.FlexibleSpace();

                    GUILayout.Label(FormatResult("{0}  (max {1})", totalOverdrawRatio, _maxTotalOverdrawRatio));
                }
            }
            else
            {
                GUILayout.Label("Available only in Play mode");
            }

            Repaint();
        }

        void Update()
        {
            if (!isEnabled)
            {
                TryShutdown();
                return;
            }

            try
            {
                if (_overlayCanvasesDirty)
                {
                    RefreshOverlayCanvases();
                }

                Camera[] activeCameras = Camera.allCameras;

                // Mirror the set of enabled user cameras without retaining destroyed
                // scene objects across level loads.
                WorldMonitor[] monitors = GetWorldMonitors();

                foreach (WorldMonitor monitor in monitors)
                {
                    if (!Array.Exists(activeCameras, c => monitor.targetCamera == c))
                    {
                        DestroyImmediate(monitor);
                    }
                }

                monitors = GetWorldMonitors();

                foreach (Camera activeCamera in activeCameras)
                {
                    if (!Array.Exists(monitors, m => m.targetCamera == activeCamera))
                    {
                        var monitor = _monitorsGo.AddComponent<WorldMonitor>();
                        monitor.SetRendererIndex(_rendererIndex);
                        monitor.SetTargetCamera(activeCamera);

                        if (!monitor.isReady)
                        {
                            DestroyImmediate(monitor);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                TryShutdown();
            }
        }

        void Init()
        {
            if (_monitorsGo != null)
            {
                throw new Exception("Attempt to start overdraw monitor twice");
            }

            if (!TryGetRendererSetup(out _rendererIndex, out _, out _, out string setupMessage))
            {
                throw new InvalidOperationException(setupMessage);
            }

            try
            {
                _monitorsGo = new GameObject("Overdraw Monitor");
                _monitorsGo.hideFlags = HideFlags.HideAndDontSave;
                _stats = new Dictionary<BaseMonitor, OverdrawStats>();
                _maxTotalOverdrawRatio = 0f;

                var overlayMonitor = _monitorsGo.AddComponent<OverlayCanvasMonitor>();
                overlayMonitor.SetRendererIndex(_rendererIndex);

                if (!overlayMonitor.isReady)
                {
                    throw new InvalidOperationException("Could not initialize the Overlay Canvas monitor.");
                }

                RefreshOverlayCanvases();
            }
            catch
            {
                TryShutdown();
                throw;
            }
        }

        void TryShutdown()
        {
            if (_monitorsGo != null)
            {
                DestroyImmediate(_monitorsGo);
            }

            _monitorsGo = null;
            _stats = null;
            _overlayCanvasesDirty = false;
            _rendererIndex = -1;
        }

        void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode
             || state == PlayModeStateChange.EnteredEditMode)
            {
                TryShutdown();
            }
        }

        BaseMonitor[] GetAllMonitors()
        {
            return _monitorsGo.GetComponentsInChildren<BaseMonitor>(true);
        }

        WorldMonitor[] GetWorldMonitors()
        {
            return _monitorsGo.GetComponentsInChildren<WorldMonitor>(true);
        }

        void MarkOverlayCanvasesDirty()
        {
            _overlayCanvasesDirty = _monitorsGo != null;
        }

        void RefreshOverlayCanvases()
        {
            if (_monitorsGo == null)
            {
                return;
            }

            Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var monitor = _monitorsGo.GetComponent<OverlayCanvasMonitor>();

            if (monitor != null)
            {
                monitor.SetTargetCanvases(canvases);
            }

            _overlayCanvasesDirty = false;
        }

        void ResetStats()
        {
            _stats.Clear();
            _maxTotalOverdrawRatio = 0f;
        }

        string FormatResult(string format, params float[] args)
        {
            var stringArgs = new List<string>();

            foreach (float arg in args)
            {
                stringArgs.Add($"{arg:N3}");
            }

            return string.Format(format, stringArgs.ToArray());
        }

        static bool TryGetRendererSetup(out int rendererIndex, out UniversalRenderPipelineAsset pipelineAsset,
            out ScriptableRendererData rendererData, out string message)
        {
            rendererIndex = -1;
            pipelineAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            rendererData = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererAssetPath);

            if (pipelineAsset == null)
            {
                message = "The active Render Pipeline Asset is not a Universal Render Pipeline asset.";
                return false;
            }

            if (rendererData == null)
            {
                message = $"The packaged renderer could not be loaded from {RendererAssetPath}.";
                return false;
            }

            // Resolve by asset identity rather than by a fragile hard-coded list index.
            ReadOnlySpan<ScriptableRendererData> renderers = pipelineAsset.rendererDataList;

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == rendererData)
                {
                    rendererIndex = i;
                    message = null;
                    return true;
                }
            }

            message = "The Overdraw Renderer is not present in the active URP Asset's Renderer List.";
            return false;
        }

        static void AddRendererToPipeline(UniversalRenderPipelineAsset pipelineAsset,
            ScriptableRendererData rendererData)
        {
            // The package asset remains read-only; only the user's URP Asset receives
            // a reference to it, and the operation participates in Editor undo.
            Undo.RecordObject(pipelineAsset, "Add Overdraw Renderer");

            var serializedPipeline = new SerializedObject(pipelineAsset);
            SerializedProperty rendererList = serializedPipeline.FindProperty("m_RendererDataList");

            if (rendererList == null)
            {
                Debug.LogError("Could not find the URP Renderer List on the active pipeline asset.");
                return;
            }

            int newIndex = rendererList.arraySize;
            rendererList.InsertArrayElementAtIndex(newIndex);

            rendererList.GetArrayElementAtIndex(newIndex)
               .objectReferenceValue = rendererData;

            serializedPipeline.ApplyModifiedProperties();
            EditorUtility.SetDirty(pipelineAsset);
        }

        [MenuItem("Tools/Overdraw Monitor")]
        static void ShowWindow()
        {
            var window = GetWindow<Window>();
            window.titleContent = new GUIContent("Overdraw Monitor");
            window.Show();
        }
    }
}
