using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace Sonosthesia.Signals.Timeline.Tests
{
    public sealed class ManualControlTrackTests
    {
        private const string Root = "Assets/Phase8ManualCurveTests";
        private const string Path = Root + "/Manual.playable";

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(Root))
                AssetDatabase.CreateFolder("Assets", "Phase8ManualCurveTests");
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(Path);
            if (AssetDatabase.IsValidFolder(Root))
                AssetDatabase.DeleteAsset(Root);
        }

        [Test]
        public void Clip_ExposesStableTemplateValueCurveProperty()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, Path);
            ManualControlTrack track = timeline.CreateTrack<ManualControlTrack>(
                null,
                "Manual Curves");
            TimelineClip clip = track.CreateClip<ManualControlPlayableAsset>();
            var asset = clip.asset as ManualControlPlayableAsset;
            asset.Configure("manual.radius", 1f);
            clip.CreateCurves("Manual Radius");
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(Path, ImportAssetOptions.ForceSynchronousImport);

            var reopened = AssetDatabase.LoadAssetAtPath<TimelineAsset>(Path);
            TimelineClip reopenedClip = null;
            foreach (TrackAsset reopenedTrack in reopened.GetRootTracks())
            {
                if (reopenedTrack is not ManualControlTrack)
                    continue;
                foreach (TimelineClip candidate in reopenedTrack.GetClips())
                {
                    reopenedClip = candidate;
                    break;
                }
            }

            Assert.That(reopenedClip, Is.Not.Null);
            var reopenedAsset = reopenedClip.asset as ManualControlPlayableAsset;
            SerializedProperty template = new SerializedObject(reopenedAsset)
                .FindProperty("template");
            Assert.That(template, Is.Not.Null);
            Assert.That(template.FindPropertyRelative("value"), Is.Not.Null);
            Assert.That(reopenedClip.curves, Is.Not.Null);
            Assert.That(reopenedAsset.SignalName, Is.EqualTo("manual.radius"));
        }

        [Test]
        public void Mixer_ProfileContainsEachNamedClip()
        {
            var mixer = new ManualControlMixerBehaviour();
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            ManualControlTrack track = timeline.CreateTrack<ManualControlTrack>();
            TimelineClip first = track.CreateClip<ManualControlPlayableAsset>();
            TimelineClip second = track.CreateClip<ManualControlPlayableAsset>();
            (first.asset as ManualControlPlayableAsset).Configure("manual.radius");
            (second.asset as ManualControlPlayableAsset).Configure("manual.hue");

            mixer.Initialize(track.GetClips(), 2);

            Assert.That(mixer.Profile.ContinuousCount, Is.EqualTo(2));
            Assert.That(mixer.Profile.GetContinuous(0).Name, Is.EqualTo("manual.radius"));
            Assert.That(mixer.Profile.GetContinuous(1).Name, Is.EqualTo("manual.hue"));
        }
    }
}
