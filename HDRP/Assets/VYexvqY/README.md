# VYexvqY Unity Migration Assets

This bundle recreates the "Neural Nexus" three.js prototype inside Unity 6.3 with support for both Universal Render Pipeline (URP) and High Definition Render Pipeline (HDRP). It focuses on GPU driven morph targets, emissive point clouds, and reactive trail motion while staying project agnostic.

## Asset Overview

### Scripts
- [Scripts/NeuralNexusController.cs](Scripts/NeuralNexusController.cs) – Manager that allocates structured buffers, drives the compute shader, handles palette generation, and issues procedural draws.

### Compute Shader (Pipeline-Agnostic)
- [Shaders/NeuralNexusUpdate.compute](Shaders/NeuralNexusUpdate.compute) – GPU kernel that builds the harmonic sphere, toroidal vortex, and nebula core layouts plus matching trails and background points. **Works with both URP and HDRP** as compute shaders don't depend on the render pipeline.

### URP Shaders
- [Shaders/URP/NeuralNexusNodes.shader](Shaders/URP/NeuralNexusNodes.shader) – URP additive point shader with adjustable glow intensity for the primary data cloud.
- [Shaders/URP/NeuralNexusTrails.shader](Shaders/URP/NeuralNexusTrails.shader) – Companion additive shader tuned for the animated trail sprites.
- [Shaders/URP/NeuralNexusBackdrop.shader](Shaders/URP/NeuralNexusBackdrop.shader) – Soft additive shader for the distant particle shell.

### HDRP Shaders
- [Shaders/HDRP/NeuralNexusNodes.shader](Shaders/HDRP/NeuralNexusNodes.shader) – HDRP additive point shader with adjustable glow intensity for the primary data cloud.
- [Shaders/HDRP/NeuralNexusTrails.shader](Shaders/HDRP/NeuralNexusTrails.shader) – Companion additive shader tuned for the animated trail sprites.
- [Shaders/HDRP/NeuralNexusBackdrop.shader](Shaders/HDRP/NeuralNexusBackdrop.shader) – Soft additive shader for the distant particle shell.

## Suggested Scene Setup

1. Import the scripts and shaders into a URP or HDRP project targeting Unity 6.3.
2. Create three materials using the shaders matching your render pipeline:
   - **For URP:**
     - Nodes → Sonosthesia/VYexvqY/URP/NeuralNexusNodes
     - Trails → Sonosthesia/VYexvqY/URP/NeuralNexusTrails
     - Backdrop → Sonosthesia/VYexvqY/URP/NeuralNexusBackdrop
   - **For HDRP:**
     - Nodes → Sonosthesia/VYexvqY/HDRP/NeuralNexusNodes
     - Trails → Sonosthesia/VYexvqY/HDRP/NeuralNexusTrails
     - Backdrop → Sonosthesia/VYexvqY/HDRP/NeuralNexusBackdrop
3. Add an empty GameObject at world origin and attach NeuralNexusController. Assign the compute shader and the three materials.
4. (Optional) Parent further empty children if you want to separate draw layer masks for nodes, trails, or the background.
5. Enable Bloom in your renderer asset or post-process stack to match the original glow. No custom renderer feature is required.

The controller registers with RenderPipelineManager and renders procedurally, so no meshes are needed. Counts default to 28k nodes, 10k trails, and 3k backdrop points, matching the HTML scene.

## Runtime Controls

NeuralNexusController exposes simple entry points for UI or timeline hooks:

```csharp
public void CycleMode();
public void SetMode(int index);
public void SetDataFlow(bool enabled);
public void SetTrailsVisible(bool visible);
public void SetBackgroundVisible(bool visible);
public void SetGlowEnabled(bool enabled);
public void SetModeBaseColor(int modeIndex, Color color);
```

- **Mode changes** blend geometry, colour, and size data on the GPU using the same easing profile as the three.js transform sequence.
- **Data flow toggle** pauses the rotational drift that keeps each mode alive when idle.
- **Glow toggle** lerps between the enabled/disabled intensities that feed the node shader.
- **Base colour updates** regenerate a ten-swatch palette using the Orbitron-style hue spread from the web prototype, so UI colour pickers can stay single-channel.

## Inspector Highlights

- **Counts** can be dialled down for profiling. Dispatch group sizes adapt automatically.
- **Trail opacity range** controls the additive streak energy while the trail visibility toggle avoids unnecessary compute work.
- **Background** radius and twinkle sliders regulate density and motion without touching shader code.
- **Bounds radius** should roughly match the farthest orbit so culling remains correct for procedural draws.

## Extensibility Notes

- Hook Cinemachine or Timeline once the render stack is integrated; the controller respects transform changes every frame.
- To push into XR later, swap the point shaders for VFX Graph equivalents while reusing the compute buffers, keeping palette generation on the CPU.
- The palette generator can be replaced with artist-authored gradients by disabling auto regeneration and populating the palette array directly before calling UploadModeData.

## Troubleshooting 

Seems https://docs.unity3d.com/6000.3/Documentation/ScriptReference/GraphicsBuffer.Target.Structured.html
DirectX 11 does not allow Index or ::Vertex buffers to also be Structured. For compute shader mesh data access with DirectX 11 compatibility, it is best to use Raw.