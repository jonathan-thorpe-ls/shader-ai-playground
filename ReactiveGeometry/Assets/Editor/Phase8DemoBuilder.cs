using System.Linq;
using Sonosthesia.Signals;
using Sonosthesia.Signals.Editor;
using Sonosthesia.Signals.Timeline;
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
    public static class Phase8DemoBuilder
    {
        private const string Folder = "Assets/TestScenes/Phase8";
        private const string ScenePath = Folder + "/Phase8-HybridSignals.unity";
        private const string GraphPath = Folder + "/Phase8-TypedControls.controlgraph";
        private const string TimelinePath = Folder + "/Phase8-Timeline.playable";
        private const string AnalysisPath = Folder + "/Synthetic-Xas.asset";
        private const string MaterialPath = Folder + "/Phase8-Lit.mat";

        [MenuItem("Sonosthesia/Phase 8/Create Hybrid Typed Controls Demo")]
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
                AssetDatabase.CreateFolder("Assets/TestScenes", "Phase8");
        }

        private static XasAnalysisAsset CreateAnalysisAsset()
        {
            var asset = AssetDatabase.LoadAssetAtPath<XasAnalysisAsset>(AnalysisPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<XasAnalysisAsset>();
                AssetDatabase.CreateAsset(asset, AnalysisPath);
            }
            asset.Configure(
                "Phase7/Synthetic.xas",
                "Phase 8 Synthetic XAS",
                187.0 * 512.0 / 48000.0);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static CompiledControlGraph CreateGraph()
        {
            ControlSignalGraph graph =
                GraphDatabase.LoadGraph<ControlSignalGraph>(GraphPath)
                ?? GraphDatabase.CreateGraph<ControlSignalGraph>(GraphPath);
            graph.UndoBeginRecordGraph("Build Phase 8 typed control graph");
            foreach (INode node in graph.GetNodes().ToArray())
                graph.RemoveNode(node);

            var rms = new XasRmsNode { Position = new Vector2(0, 0) };
            var rmsRemap = new RemapNode { Position = new Vector2(250, 0) };
            var manualRadius = new ContinuousSignalInputNode
            {
                Position = new Vector2(0, 120)
            };
            var addRadius = new AddNode { Position = new Vector2(500, 50) };
            var manualAngle = new ContinuousSignalInputNode
            {
                Position = new Vector2(250, 180)
            };
            var polar = new PolarVector2Node { Position = new Vector2(750, 50) };
            var vector2Output = new Vector2ControlOutputNode
            {
                Position = new Vector2(1000, 50)
            };

            var lows = new XasLowsNode { Position = new Vector2(0, 340) };
            var lowsRemap = new RemapNode { Position = new Vector2(250, 340) };
            var manualVectorAngle = new ContinuousSignalInputNode
            {
                Position = new Vector2(0, 460)
            };
            var manualHeight = new ContinuousSignalInputNode
            {
                Position = new Vector2(250, 520)
            };
            var cylindrical = new CylindricalNode
            {
                Position = new Vector2(600, 410)
            };
            var vector3Output = new Vector3ControlOutputNode
            {
                Position = new Vector2(900, 410)
            };

            var manualHue = new ContinuousSignalInputNode
            {
                Position = new Vector2(0, 700)
            };
            var saturation = new ConstantNode { Position = new Vector2(0, 780) };
            var highs = new XasHighsNode { Position = new Vector2(0, 860) };
            var highsRemap = new RemapNode { Position = new Vector2(250, 860) };
            var hsv = new HsvNode { Position = new Vector2(600, 760) };
            var colorOutput = new ColorControlOutputNode
            {
                Position = new Vector2(900, 760)
            };

            Node[] nodes =
            {
                rms, rmsRemap, manualRadius, addRadius, manualAngle, polar, vector2Output,
                lows, lowsRemap, manualVectorAngle, manualHeight, cylindrical, vector3Output,
                manualHue, saturation, highs, highsRemap, hsv, colorOutput
            };
            foreach (Node node in nodes)
                graph.AddNode(node);

            manualRadius.GetNodeOptionByName("Signal").TrySetValue("manual.radius");
            manualAngle.GetNodeOptionByName("Signal").TrySetValue("manual.angle");
            manualVectorAngle.GetNodeOptionByName("Signal").TrySetValue("manual.angle3");
            manualHeight.GetNodeOptionByName("Signal").TrySetValue("manual.height");
            manualHue.GetNodeOptionByName("Signal").TrySetValue("manual.hue");

            rmsRemap.GetNodeOptionByName("Input Min").TrySetValue(-45f);
            rmsRemap.GetNodeOptionByName("Input Max").TrySetValue(-15f);
            rmsRemap.GetNodeOptionByName("Output Min").TrySetValue(0.25f);
            rmsRemap.GetNodeOptionByName("Output Max").TrySetValue(1.75f);
            lowsRemap.GetNodeOptionByName("Input Min").TrySetValue(-50f);
            lowsRemap.GetNodeOptionByName("Input Max").TrySetValue(-15f);
            lowsRemap.GetNodeOptionByName("Output Min").TrySetValue(0.2f);
            lowsRemap.GetNodeOptionByName("Output Max").TrySetValue(2f);
            highsRemap.GetNodeOptionByName("Input Min").TrySetValue(-60f);
            highsRemap.GetNodeOptionByName("Input Max").TrySetValue(-25f);
            highsRemap.GetNodeOptionByName("Output Min").TrySetValue(0.2f);
            highsRemap.GetNodeOptionByName("Output Max").TrySetValue(1f);
            saturation.GetNodeOptionByName("Value").TrySetValue(1f);
            vector2Output.GetNodeOptionByName("Name").TrySetValue("hybrid.position2");
            vector3Output.GetNodeOptionByName("Name").TrySetValue("hybrid.position3");
            colorOutput.GetNodeOptionByName("Name").TrySetValue("material.color");

            graph.Connect(rms.GetOutputPortByName("Value"), rmsRemap.GetInputPortByName("A"));
            graph.Connect(rmsRemap.GetOutputPortByName("Value"), addRadius.GetInputPortByName("A"));
            graph.Connect(manualRadius.GetOutputPortByName("Value"), addRadius.GetInputPortByName("B"));
            graph.Connect(manualAngle.GetOutputPortByName("Value"), polar.GetInputPortByName("Angle"));
            graph.Connect(addRadius.GetOutputPortByName("Value"), polar.GetInputPortByName("Radius"));
            graph.Connect(polar.GetOutputPortByName("Value"), vector2Output.GetInputPortByName("Value"));

            graph.Connect(lows.GetOutputPortByName("Value"), lowsRemap.GetInputPortByName("A"));
            graph.Connect(manualVectorAngle.GetOutputPortByName("Value"), cylindrical.GetInputPortByName("Angle"));
            graph.Connect(lowsRemap.GetOutputPortByName("Value"), cylindrical.GetInputPortByName("Radius"));
            graph.Connect(manualHeight.GetOutputPortByName("Value"), cylindrical.GetInputPortByName("Height"));
            graph.Connect(cylindrical.GetOutputPortByName("Value"), vector3Output.GetInputPortByName("Value"));

            graph.Connect(manualHue.GetOutputPortByName("Value"), hsv.GetInputPortByName("Hue"));
            graph.Connect(saturation.GetOutputPortByName("Value"), hsv.GetInputPortByName("Saturation"));
            graph.Connect(highs.GetOutputPortByName("Value"), highsRemap.GetInputPortByName("A"));
            graph.Connect(highsRemap.GetOutputPortByName("Value"), hsv.GetInputPortByName("Value"));
            graph.Connect(hsv.GetOutputPortByName("Value"), colorOutput.GetInputPortByName("Value"));

            GraphDatabase.SaveGraph(graph);
            graph.UndoEndRecordGraph();
            AssetDatabase.ImportAsset(GraphPath, ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadMainAssetAtPath(GraphPath) as CompiledControlGraph;
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
            TimelineClip xasClip = xasTrack.CreateClip<XasPlayableAsset>();
            xasClip.start = 0;
            xasClip.duration = analysis.DurationSeconds;
            (xasClip.asset as XasPlayableAsset).Configure(analysis);

            ManualControlTrack manualTrack =
                timeline.CreateTrack<ManualControlTrack>(null, "Manual Curves");
            AddManualClip(manualTrack, "manual.radius", 0.5f, analysis.DurationSeconds);
            AddManualClip(manualTrack, "manual.angle", 1f, analysis.DurationSeconds);
            AddManualClip(manualTrack, "manual.angle3", 0.2f, analysis.DurationSeconds);
            AddManualClip(manualTrack, "manual.height", 0.75f, analysis.DurationSeconds);
            AddManualClip(manualTrack, "manual.hue", 0.1f, analysis.DurationSeconds);

            EditorUtility.SetDirty(timeline);
            return timeline;
        }

        private static void AddManualClip(
            ManualControlTrack track,
            string signalName,
            float value,
            double duration)
        {
            TimelineClip clip = track.CreateClip<ManualControlPlayableAsset>();
            clip.displayName = signalName;
            clip.start = 0;
            clip.duration = duration;
            (clip.asset as ManualControlPlayableAsset).Configure(signalName, value);
            clip.CreateCurves(signalName);
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
            cameraObject.transform.position = new Vector3(0, 1, -7);
            cameraObject.AddComponent<Camera>();
            var lightObject = new GameObject("Directional Light");
            lightObject.transform.rotation = Quaternion.Euler(45, -30, 0);
            lightObject.AddComponent<Light>().type = LightType.Directional;

            var vector2 = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            vector2.name = "Vector2 Marker";
            vector2.transform.localScale = Vector3.one * 0.2f;
            var vector3 = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            vector3.name = "Vector3 Marker";
            vector3.transform.localScale = Vector3.one * 0.2f;
            vector3.transform.position = new Vector3(1f, 0f, 0f);
            var colorObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            colorObject.name = "URP Lit Color Target";
            colorObject.transform.position = new Vector3(0f, 2f, 0f);
            colorObject.transform.localScale = Vector3.one * 1.25f;
            MeshRenderer colorRenderer = colorObject.GetComponent<MeshRenderer>();
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"))
                {
                    name = "Phase8 Lit",
                    color = Color.white
                };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            colorRenderer.sharedMaterial = material;

            var controlTargetObject = new GameObject("Phase 8 Control Target");
            ControlSignalTarget target =
                controlTargetObject.AddComponent<ControlSignalTarget>();
            target.BindGraph(graph);
            HybridControlConsumer hybrid =
                controlTargetObject.AddComponent<HybridControlConsumer>();
            hybrid.Configure(vector2.transform, vector3.transform);
            UrpLitColorControlSlot colorSlot =
                colorObject.AddComponent<UrpLitColorControlSlot>();
            colorSlot.Configure(colorRenderer);

            var vector2Route = new ControlRouteBinding();
            vector2Route.Configure(
                "hybrid.position2",
                hybrid,
                "hybrid.position2");
            var vector3Route = new ControlRouteBinding();
            vector3Route.Configure(
                "hybrid.position3",
                hybrid,
                "hybrid.position3");
            var colorRoute = new ControlRouteBinding();
            colorRoute.Configure(
                "material.color",
                colorSlot,
                "material.color");
            target.SetRoutes(new[] { vector2Route, vector3Route, colorRoute });

            var directorObject = new GameObject("Phase 8 Timeline");
            PlayableDirector director =
                directorObject.AddComponent<PlayableDirector>();
            director.playableAsset = timeline;
            director.playOnAwake = true;
            foreach (PlayableBinding output in timeline.outputs)
                director.SetGenericBinding(output.sourceObject, target);

            EditorSceneManager.SaveScene(scene, ScenePath);
        }
    }
}
