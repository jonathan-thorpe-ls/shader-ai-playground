# Sonosthesia XAS Reader

`com.sonosthesia.xas` reads `.xas` audio-analysis files without loading frame
and event payloads into memory. It is independent of Timeline and other editor
APIs so the same reader can back timeline tracks, runtime players, XR
applications, and tools.

## Usage

```csharp
using var reader = new XasReader("Assets/Audio/MyTrack.xas");

if (reader.TryEvaluateContinuous(playheadSeconds, out XasContinuousFrame frame))
{
    // Interpolated rms, lows, mids, highs, and centroid.
}

var peakBuffer = new XasPeakEvent[64];
int peakCount = reader.GetEventsOverlapping(
    rangeStartSeconds,
    rangeEndSeconds,
    peakBuffer);
```

`TryEvaluateContinuous` and event queries allocate no managed objects after the
required chunks have been cached. Event queries write into caller-owned memory
and report `peakCount`. If the range contains more events than the destination,
the returned count is truncated; use `GetEventCountOverlapping` to size a buffer.

The reader owns its file stream and is not thread-safe. Dispose it when the
analysis source is unloaded.

Timeline tracks, clips, importers, and assets are intentionally outside this
package's scope.

The format specification is maintained in the
`sonosthesia-audio-pipeline` repository at
`schemas/streamingbinary.md`.
