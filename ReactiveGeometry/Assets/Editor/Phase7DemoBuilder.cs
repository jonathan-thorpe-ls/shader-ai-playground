using System.Linq;
using Sonosthesia.ProcGeo;
using Sonosthesia.Signals;
using Sonosthesia.Signals.Editor;
using Sonosthesia.Signals.Xas;
using Sonosthesia.Signals.Xas.Editor;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

namespace Sonosthesia.Editor
{
    public static class Phase7DemoBuilder
    {
        private const string Folder = "Assets/TestScenes/Phase7";
        private const string ScenePath = Folder + "/Phase7-XasSignals.unity";
        private const string GraphPath = Folder + "/Phase7-ControlSignals.controlgraph";
        private const string TimelinePath = Folder + "/Phase7-Timeline.playable";
        private const string AnalysisPath = Folder + "/Synthetic-Xas.asset";

        [MenuItem("Sonosthesia/Phase 7/Create XAS Timeline Demo")]
        public static void Create()
        {
            EnsureFolder();
            XasAnalysisAsset analysis = CreateAnalysisAsset();
            CompiledControlGraph graph = CreateGraph();
            TimelineAsset timeline = CreateTimeline(analysis);
            CreateScene(graph, timeline);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/TestScenes", "Phase7");
        }

        private static XasAnalysisAsset CreateAnalysisAsset()
        {
            var analysis = AssetDatabase.LoadAssetAtPath<XasAnalysisAsset>(AnalysisPath);
            if (analysis == null)
            {
                analysis = ScriptableObject.CreateInstance<XasAnalysisAsset>();
                AssetDatabase.CreateAsset(analysis, AnalysisPath);
            }
            analysis.Configure(
                "Phase7/Synthetic.xas",
                "Synthetic Phase 7 XAS",
                187.0 * 512.0 / 48000.0);
            EditorUtility.SetDirty(analysis);
            return analysis;
        }

        private static CompiledControlGraph CreateGraph()
        {
            ControlSignalGraph graph =
                GraphDatabase.LoadGraph<ControlSignalGraph>(GraphPath)
                ?? GraphDatabase.CreateGraph<ControlSignalGraph>(GraphPath);
            graph.UndoBeginRecordGraph("Build Phase 7 demo graph");

            foreach (INode node in graph.GetNodes().ToArray())
                graph.RemoveNode(node);

            var rms = new XasRmsNode { Position = new Vector2(0, 0) };
            var remap = new RemapNode { Position = new Vector2(280, 0) };
            var fieldOutput = new FloatControlOutputNode
            {
                Position = new Vector2(560, 0)
            };
            var lows = new XasLowsNode { Position = new Vector2(0, 180) };
            var lowsRemap = new RemapNode { Position = new Vector2(280, 180) };
            var lowEnvelope = new AttackReleaseNode { Position = new Vector2(560, 180) };
            var stiffnessOutput = new FloatControlOutputNode
            {
                Position = new Vector2(840, 180)
            };
            var pulse = new XasMainPulseNode { Position = new Vector2(0, 360) };
            var pulseOutput = new PulseControlOutputNode
            {
                Position = new Vector2(560, 360)
            };

            graph.AddNode(rms);
            graph.AddNode(remap);
            graph.AddNode(fieldOutput);
            graph.AddNode(lows);
            graph.AddNode(lowsRemap);
            graph.AddNode(lowEnvelope);
            graph.AddNode(stiffnessOutput);
            graph.AddNode(pulse);
            graph.AddNode(pulseOutput);

            remap.GetNodeOptionByName("Input Min").TrySetValue(-45f);
            remap.GetNodeOptionByName("Input Max").TrySetValue(-15f);
            remap.GetNodeOptionByName("Output Min").TrySetValue(1f);
            remap.GetNodeOptionByName("Output Max").TrySetValue(18f);
            lowEnvelope.GetNodeOptionByName("Attack").TrySetValue(0.03f);
            lowEnvelope.GetNodeOptionByName("Release").TrySetValue(0.25f);
            lowsRemap.GetNodeOptionByName("Input Min").TrySetValue(-50f);
            lowsRemap.GetNodeOptionByName("Input Max").TrySetValue(-15f);
            lowsRemap.GetNodeOptionByName("Output Min").TrySetValue(40f);
            lowsRemap.GetNodeOptionByName("Output Max").TrySetValue(180f);
            fieldOutput.GetNodeOptionByName("Name")
                .TrySetValue("procgeo.field.pulseAmplitude");
            stiffnessOutput.GetNodeOptionByName("Name")
                .TrySetValue("procgeo.simulation.stiffness");
            pulseOutput.GetNodeOptionByName("Name")
                .TrySetValue("procgeo.impulse.trigger");

            graph.Connect(
                rms.GetOutputPortByName("Value"),
                remap.GetInputPortByName("A"));
            graph.Connect(
                remap.GetOutputPortByName("Value"),
                fieldOutput.GetInputPortByName("Value"));
            graph.Connect(
                lows.GetOutputPortByName("Value"),
                lowsRemap.GetInputPortByName("A"));
            graph.Connect(
                lowsRemap.GetOutputPortByName("Value"),
                lowEnvelope.GetInputPortByName("A"));
            graph.Connect(
                lowEnvelope.GetOutputPortByName("Value"),
                stiffnessOutput.GetInputPortByName("Value"));
            graph.Connect(
                pulse.GetOutputPortByName("Value"),
                pulseOutput.GetInputPortByName("Value"));

            GraphDatabase.SaveGraph(graph);
            graph.UndoEndRecordGraph();
            AssetDatabase.ImportAsset(
                GraphPath,
                ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadMainAssetAtPath(GraphPath)
                as CompiledControlGraph;
        }

        private static TimelineAsset CreateTimeline(XasAnalysisAsset analysis)
        {
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath);
            if (timeline == null)
            {
                timeline = ScriptableObject.CreateInstance<TimelineAsset>();
                AssetDatabase.CreateAsset(timeline, TimelinePath);
            }

            foreach (TrackAsset track in timeline.GetRootTracks())
                ScriptableObject.DestroyImmediate(track, true);

            XasTrack xasTrack = timeline.CreateTrack<XasTrack>(null, "XAS Signals");
            TimelineClip clip = xasTrack.CreateClip<XasPlayableAsset>();
            clip.start = 0;
            clip.duration = analysis.DurationSeconds;
            clip.displayName = analysis.DisplayName;
            var playable = clip.asset as XasPlayableAsset;
            playable.Configure(analysis);
            EditorUtility.SetDirty(timeline);
            return timeline;
        }

        private static void CreateScene(
            CompiledControlGraph graph,
            TimelineAsset timeline)
        {
            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0, 1, -5);
            cameraObject.AddComponent<Camera>();
            var lightObject = new GameObject("Directional Light");
            lightObject.transform.rotation = Quaternion.Euler(45, -30, 0);
            lightObject.AddComponent<Light>().type = LightType.Directional;

            var geometryObject = new GameObject("Procedural Geometry");
            ProceduralMeshRenderer renderer =
                geometryObject.AddComponent<ProceduralMeshRenderer>();
            ImpulseWaveSource impulse = geometryObject.AddComponent<ImpulseWaveSource>();
            impulse.Configure(renderer);
            ProceduralMeshControlSlots slots =
                geometryObject.AddComponent<ProceduralMeshControlSlots>();
            slots.Configure(renderer, impulse);
            GeometryFieldSettings fieldSettings = renderer.FieldSettings;
            fieldSettings.fieldsEnabled = true;
            fieldSettings.pulseEnabled = true;
            renderer.SetFieldSettings(fieldSettings);

            var controlObject = new GameObject("XAS Control Target");
            ControlSignalTarget target =
                controlObject.AddComponent<ControlSignalTarget>();
            target.BindGraph(graph);
            var fieldRoute = new ControlRouteBinding();
            fieldRoute.Configure(
                "procgeo.field.pulseAmplitude",
                slots,
                "procgeo.field.pulseAmplitude");
            var stiffnessRoute = new ControlRouteBinding();
            stiffnessRoute.Configure(
                "procgeo.simulation.stiffness",
                slots,
                "procgeo.simulation.stiffness",
                3f);
            var impulseRoute = new ControlRouteBinding();
            impulseRoute.Configure(
                "procgeo.impulse.trigger",
                slots,
                "procgeo.impulse.trigger");
            target.SetRoutes(new[] { fieldRoute, stiffnessRoute, impulseRoute });

            var directorObject = new GameObject("Phase 7 Timeline");
            PlayableDirector director =
                directorObject.AddComponent<PlayableDirector>();
            director.playableAsset = timeline;
            director.playOnAwake = true;
            foreach (PlayableBinding output in timeline.outputs)
            {
                director.SetGenericBinding(output.sourceObject, target);
                break;
            }

            EditorSceneManager.SaveScene(scene, ScenePath);
        }
    }
}
