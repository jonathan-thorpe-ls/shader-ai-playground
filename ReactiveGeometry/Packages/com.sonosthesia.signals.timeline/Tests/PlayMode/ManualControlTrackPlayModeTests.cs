using System;
using System.Collections;
using NUnit.Framework;
using Sonosthesia.Signals;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.TestTools;
using UnityEngine.Timeline;

namespace Sonosthesia.Signals.Timeline.Tests
{
    public sealed class ManualControlTrackPlayModeTests
    {
        [UnityTest]
        public IEnumerator TrackFeedsNamedManualValueIntoTarget()
        {
            var profile = new ControlSignalProfile(
                "Manual PlayMode",
                new[]
                {
                    new ControlSignalDescriptor
                    {
                        Name = "manual.radius",
                        Type = ControlSignalType.Continuous
                    }
                },
                Array.Empty<ControlSignalDescriptor>());
            CompiledControlGraph graph = ScriptableObject.CreateInstance<CompiledControlGraph>();
            graph.Configure(
                profile,
                new[]
                {
                    new ControlOperation
                    {
                        Kind = ControlOperationKind.ContinuousSource,
                        OutputType = ControlSignalType.Continuous,
                        InputA = -1,
                        InputB = -1,
                        InputC = -1,
                        InputD = -1,
                        SourceIndex = 0
                    }
                },
                new[]
                {
                    new ControlGraphOutput
                    {
                        Name = "manual.output",
                        Id = ControlSignalId.FromString("manual.output").Value,
                        OperationIndex = 0,
                        Type = ControlSignalType.Continuous
                    }
                },
                0,
                0);

            var targetObject = new GameObject("Manual Target");
            ControlSignalTarget target =
                targetObject.AddComponent<ControlSignalTarget>();
            target.BindGraph(graph);
            var director = targetObject.AddComponent<PlayableDirector>();
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            ManualControlTrack track =
                timeline.CreateTrack<ManualControlTrack>(null, "Manual");
            TimelineClip clip = track.CreateClip<ManualControlPlayableAsset>();
            clip.duration = 0.5;
            (clip.asset as ManualControlPlayableAsset)
                .Configure("manual.radius", 0.75f);
            director.playableAsset = timeline;
            director.extrapolationMode = DirectorWrapMode.Hold;
            foreach (PlayableBinding output in timeline.outputs)
                director.SetGenericBinding(output.sourceObject, target);

            director.time = 0.1;
            director.Play();
            yield return null;
            yield return new WaitForEndOfFrame();

            Assert.That(
                target.TryGetOutput(0, out float value, out _),
                Is.True);
            Assert.That(value, Is.EqualTo(0.75f).Within(0.0001f));

            UnityEngine.Object.Destroy(timeline);
            UnityEngine.Object.Destroy(graph);
            UnityEngine.Object.Destroy(targetObject);
        }
    }
}
