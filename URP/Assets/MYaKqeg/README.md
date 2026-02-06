# MYaKqeg Unity Migration Assets

This package mirrors the interactive wireframe demo from the original three.js prototype using Unity 6.3 + URP building blocks. It provides procedural geometry, morph animation, shader-driven pulses, and a supporting starfield without requiring a full project scaffold.

## Asset Overview

- [Scripts/ColorPaletteDefinition.cs](Scripts/ColorPaletteDefinition.cs) – ScriptableObject storing thematic color sets used across meshes.
- [Scripts/IcoSphereBuilder.cs](Scripts/IcoSphereBuilder.cs) – Utility that generates the subdivided icosphere topology shared by nodes and connections.
- [Scripts/MorphingWireframeController.cs](Scripts/MorphingWireframeController.cs) – Core behaviour that builds procedural meshes, manages morph targets, drives palette changes, and streams pulse data to shaders.
- [Scripts/StarfieldController.cs](Scripts/StarfieldController.cs) – Lightweight starfield generator matching the background point cloud.
- [Shaders/MorphingWirePoints.shader](Shaders/MorphingWirePoints.shader) – URP additive billboard shader for node sprites with GPU-side morph and pulse response.
- [Shaders/MorphingWireLines.shader](Shaders/MorphingWireLines.shader) – URP additive ribbon shader for connection segments with pulse amplification.
- [Shaders/StarfieldPoints.shader](Shaders/StarfieldPoints.shader) – URP additive billboard shader for the rotating backdrop points with procedural twinkle.

## Suggested Scene Setup

1. **Import** the scripts and shaders into a URP project targeting Unity 6.3.
2. **Create materials** for each shader:
   - Nodes → `Sonosthesia/MYaKqeg/MorphingWirePoints` (adjust base size and pulse gain as desired).
   - Lines → `Sonosthesia/MYaKqeg/MorphingWireLines` (set line width, opacity, and pulse boost).
   - Starfield → `Sonosthesia/MYaKqeg/StarfieldPoints` (tune tint and twinkle parameters).
3. **Author colour themes** by creating `ColorPaletteDefinition` assets (Assets/Create/Sonosthesia/Color Palette). Populate each with four colours to mimic the original HTML palettes.
4. **Build the hierarchy**:
   - Root GameObject with `MorphingWireframeController`.
   - Child `Nodes` object with `MeshFilter`, `MeshRenderer`, and the nodes material.
   - Child `Connections` object with `MeshFilter`, `MeshRenderer`, and the lines material.
   - Assign the two mesh components plus desired palettes to the controller.
5. **Optional background**: Create a `Starfield` object with `StarfieldController`, a `MeshFilter`, and the starfield material. Parenting under the root keeps transforms aligned.

Once referenced, `MorphingWireframeController` will build meshes on load, randomise colours per palette, and keep data updated via Material Property Blocks. The starfield script procedurally scatters points inside a configurable volume and regenerates them in edit mode for quick iteration.

## Interaction Hooks

The controller exposes simple entry points so UX layers can mirror the web demo:

```
public void TriggerPulse(Vector2 screenPosition);
public void SetMorphTarget(bool star);
public void ToggleMorph();
public void SetPalette(int paletteIndex);
```

- **Pulses**: Call `TriggerPulse` with screen coordinates (e.g. from the Input System pointer event). The script projects onto the morphable sphere, reusing the GLSL pulse formula.
- **Morph toggle**: Wire UI buttons or key events to `SetMorphTarget`/`ToggleMorph` to animate between sphere and star blends.
- **Palette switching**: Bind UI controls to `SetPalette`. The controller re-rolls vertex colours using the selected palette definition for both nodes and lines.

## Timing & Visual Notes

- Pulse speed, lifetime, and damping mirror the original JS constants and can be tweaked directly on the controller.
- Star shape modulation replicates the sinusoidal spike logic used in the three.js version, ensuring feature parity for silhouette transitions.
- Shaders implement additive blending and per-vertex colour mixing; combine with URP bloom if you want the extra glow seen in the HTML demo.
- Geometry subdivision defaults to level 4 (roughly matching the source). Increase subdivisions cautiously – triangle counts rise quickly.

## Extending Further

- Use Timeline or Cinemachine for camera choreography once the visual stack is integrated.
- Replace the manual palette reseed with deterministic gradients or texture-driven ramps if tighter art direction is required.
- For XR readiness, evaluate substituting the geometry shaders with VFX Graph equivalents to take advantage of platform-specific optimisations.
