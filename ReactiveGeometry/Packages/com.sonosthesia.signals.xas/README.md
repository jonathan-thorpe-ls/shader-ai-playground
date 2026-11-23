# Sonosthesia XAS Timeline Signals

Bridges `com.sonosthesia.xas` into the provider-neutral
`com.sonosthesia.signals` package.

Create an `XasAnalysisAsset` and point it to an `.xas` file below
`Assets/StreamingAssets`, then add an `XasTrack` to a Timeline and bind it to a
`ControlSignalTarget`.

Overlapping clips use normalized continuous blending, merge pulse strengths by
maximum, and mark forward pulse starts. XAS reader and Timeline details remain
outside the core graph/runtime package.
