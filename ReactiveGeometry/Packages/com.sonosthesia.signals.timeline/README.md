# Sonosthesia Manual Curve Signals

Provides `ManualControlTrack` for authoring named float curves in Unity
Timeline's native Curves View. The track binds to `ControlSignalTarget` and
does not depend on XAS or procedural geometry.

Each manual clip contains one named curve backed by the
`ManualControlBehaviour.value` template field. Unity records that field in the
clip's AnimationClip through Timeline's Curves View, and the mixer blends clips
with the same signal name into a provider-neutral profile.

Unity 6000.6 did not preserve dynamic list-element curve bindings through the
public serialized-playable API, so the package intentionally uses unlimited
single-value clips instead of a fixed or unreliable channel bank.
