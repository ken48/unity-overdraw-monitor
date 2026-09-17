# Unity Overdraw Monitor

Unity Overdraw Monitor is an Editor tool for measuring how many rasterized fragments URP cameras and Unity UI produce in the current frame. It renders the scene with additive debug materials, reduces the result on the GPU, and expresses the fragment count in full-screen equivalents.

The package is intended for profiling and content iteration. It is not a replacement for the Unity Profiler, RenderDoc, or a platform GPU profiler.

## Requirements

- Unity 6000.3 or newer
- Universal Render Pipeline 17.3 or newer
- Compute shader support
- `RFloat` render texture support

## Installation

Install the package from the Unity Package Manager using this Git URL:

```text
https://github.com/ken48/unity-overdraw-monitor.git?path=/OverdrawMonitor
```

Alternatively, add it to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "ken48.overdraw-monitor": "https://github.com/ken48/unity-overdraw-monitor.git?path=/OverdrawMonitor"
  }
}
```

## URP setup

The package contains a preconfigured renderer at:

```text
Packages/ken48.overdraw-monitor/Settings/OverdrawRenderer.asset
```

The package includes an Editor setup mechanism for this asset. Open **Tools > Overdraw Monitor** while not in Play Mode; the window checks the active URP Asset and, if necessary, exposes **Add Overdraw Renderer to Active URP Asset**. Clicking that button adds a reference to the packaged renderer automatically.

The tool locates the renderer by asset reference, so it can occupy any position in the URP Renderer List. No fixed renderer index is required.

You can also add it manually:

1. Select the URP Asset currently used by the active quality level.
2. Add `OverdrawRenderer` to its **Renderer List**.
3. Keep the packaged asset itself in the list rather than making a duplicate; automatic detection uses that asset reference.

The renderer contains two `Render Objects` features:

- **Count Opaque** uses back-face culling for ordinary opaque geometry.
- **Count Transparent** uses a two-sided material for sprites, transparent geometry, and camera-rendered UI.

## Usage

1. Enter Play Mode.
2. Open **Tools > Overdraw Monitor**.
3. Click **Start**.
4. Use **Reset Stats** to clear recorded peak values.
5. Click **Stop** when profiling is complete.

When monitoring starts, the tool measures each enabled camera separately. It also measures all root `Screen Space - Overlay` canvases on Display 1 under the monitor named `All Overlay Canvases`.

## Reading the results

The displayed value is calculated as:

```text
counted fragments in the current frame / game-view pixel count
```

An overdraw value of `1.0` therefore means that the frame produced as many counted fragments as there are pixels in one full screen. A value of `3.0` means three screens' worth of fragment writes. It does not mean that every screen pixel was necessarily written exactly three times: the fragments may be concentrated in a smaller part of the screen.

This is a per-frame quantity, not an average over time. Camera viewports smaller than the full game view contribute proportionally to their covered pixel area.

- **Overdraw** is the normalized fragment count from the latest measured frame.
- **max** is the highest value observed since monitoring started or since **Reset Stats** was clicked.
- **Total** is the sum of all active camera monitors and the Overlay Canvas monitor.

Short-lived transition screens can produce legitimate maximum spikes when outgoing and incoming content overlap.

## Overlay Canvas handling

`Screen Space - Overlay` canvases normally bypass cameras. To measure them without requiring project-specific setup, the tool temporarily:

1. switches active root Overlay Canvases to `Screen Space - Camera`;
2. assigns a hidden diagnostic camera;
3. renders them through the packaged overdraw renderer;
4. restores every modified Canvas in a `finally` block.

The tool caches only root Canvas references. It does not scan or cache every `CanvasRenderer` and does not replace live UI materials itself.

## Accuracy and limitations

- The tool supports URP only.
- The dedicated UI monitor handles root `Screen Space - Overlay` canvases on Display 1.
- `Screen Space - Camera` and `World Space` canvases are not supported by the dedicated UI monitor. If they are rendered by a user camera, they may be included in that camera's result according to the active URP renderer filters, but they are not measured as a separate guaranteed UI category.
- Replacement materials intentionally ignore textures and alpha. Transparent pixels inside a submitted quad still contribute to overdraw.
- Stencil and custom masking behavior may differ from the original material and can overestimate heavily masked UI.
- Custom render passes, unusual shader tags, or objects excluded by the packaged renderer's filters may not be counted.
- Peak values include scene-loading and UI-transition frames until manually reset.
- Monitoring performs extra scene renders and a synchronous GPU readback every frame.

## Cleanup behavior

Temporary cameras, render textures, compute buffers, and Canvas state are released or restored when:

- monitoring is stopped;
- the window is closed;
- Play Mode exits;
- scripts are recompiled or assemblies reload;
- the Editor quits;
- initialization, rendering, compute dispatch, or GPU readback throws an exception.

Temporary objects use `HideAndDontSave` and are not written into scenes.

## Package layout

```text
OverdrawMonitor/
├── Editor/       Editor window and URP setup UI
├── Materials/    Opaque and two-sided debug materials
├── Resources/    GPU reduction compute shader
├── Settings/     Preconfigured URP renderer data
├── Shaders/      Additive overdraw shader
└── Sources/      Runtime monitor components
```

The sample project in `Demo/` demonstrates the package with URP.
