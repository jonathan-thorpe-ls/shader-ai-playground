using System;
using NUnit.Framework;
using UnityEngine;

namespace Sonosthesia.Signals.Tests
{
    public sealed class TypedControlGraphTests
    {
        [Test]
        public void Executor_GeneratesTypedVectorAndColorValues()
        {
            var profile = new ControlSignalProfile(
                "Typed",
                new[]
                {
                    Descriptor("angle"),
                    Descriptor("radius"),
                    Descriptor("height"),
                    Descriptor("hue"),
                    Descriptor("saturation"),
                    Descriptor("colorValue")
                },
                Array.Empty<ControlSignalDescriptor>());
            CompiledControlGraph graph = ScriptableObject.CreateInstance<CompiledControlGraph>();
            graph.Configure(
                profile,
                new[]
                {
                    Source(0),
                    Source(1),
                    Source(2),
                    Source(3),
                    Source(4),
                    Source(5),
                    Typed(ControlOperationKind.Spherical, ControlSignalType.Vector3, 0, 1, 2),
                    Typed(ControlOperationKind.HsvToColor, ControlSignalType.Color, 3, 4, 5)
                },
                new[]
                {
                    Output("test.vector", 6, ControlSignalType.Vector3),
                    Output("test.color", 7, ControlSignalType.Color)
                },
                0,
                0);
            var executor = new ControlGraphExecutor(graph);
            var frame = new ControlSignalFrame(
                profile,
                0,
                1f / 60f,
                ControlPlaybackDirection.Forward,
                false,
                new[] { 0f, 0f, 1f, 0f, 1f, 1f },
                new byte[] { 1, 1, 1, 1, 1, 1 },
                Array.Empty<float>(),
                Array.Empty<float>(),
                Array.Empty<byte>());

            executor.Evaluate(frame);

            Assert.That(executor.TryGetOutput(0, out Vector3 vector), Is.True);
            Assert.That(vector, Is.EqualTo(new Vector3(1f, 0f, 0f)));
            Assert.That(executor.TryGetOutput(1, out Color color), Is.True);
            Assert.That(color, Is.EqualTo(Color.red));
        }

        [Test]
        public void Executor_RejectsReadingOutputAsWrongType()
        {
            CompiledControlGraph graph = ScriptableObject.CreateInstance<CompiledControlGraph>();
            graph.Configure(
                new ControlSignalProfile(
                    "Empty",
                    Array.Empty<ControlSignalDescriptor>(),
                    Array.Empty<ControlSignalDescriptor>()),
                new[]
                {
                    new ControlOperation
                    {
                        Kind = ControlOperationKind.Vector2Constant,
                        OutputType = ControlSignalType.Vector2,
                        InputA = -1,
                        InputB = -1,
                        ParameterA = 1f,
                        ParameterB = 2f
                    }
                },
                new[] { Output("test.vector2", 0, ControlSignalType.Vector2) },
                0,
                0);
            var executor = new ControlGraphExecutor(graph);
            var frame = new ControlSignalFrame(
                graph.Profile,
                0,
                1f / 60f,
                ControlPlaybackDirection.Forward,
                false,
                Array.Empty<float>(),
                Array.Empty<byte>(),
                Array.Empty<float>(),
                Array.Empty<float>(),
                Array.Empty<byte>());

            executor.Evaluate(frame);

            Assert.That(executor.TryGetOutput(0, out Vector2 vector2), Is.True);
            Assert.That(vector2, Is.EqualTo(new Vector2(1f, 2f)));
            Assert.That(executor.TryGetOutput(0, out Vector3 _), Is.False);
            Assert.That(executor.TryGetOutput(0, out Color _), Is.False);
        }

        [Test]
        public void Executor_GeneratorsAreBoundedAndDeterministic()
        {
            var profile = new ControlSignalProfile(
                "Generators",
                new[]
                {
                    Descriptor("angle"),
                    Descriptor("radius"),
                    Descriptor("height"),
                    Descriptor("amplitude"),
                    Descriptor("frequency"),
                    Descriptor("excitation")
                },
                Array.Empty<ControlSignalDescriptor>());
            CompiledControlGraph graph = ScriptableObject.CreateInstance<CompiledControlGraph>();
            graph.Configure(
                profile,
                new[]
                {
                    Source(0), Source(1), Source(2),
                    Source(3), Source(4), Source(5),
                    Typed(ControlOperationKind.Polar, ControlSignalType.Vector2, 0, 1),
                    Typed(ControlOperationKind.Cylindrical, ControlSignalType.Vector3, 0, 1, 2),
                    new ControlOperation
                    {
                        Kind = ControlOperationKind.Vector3Brownian,
                        OutputType = ControlSignalType.Vector3,
                        InputA = 3,
                        InputB = 4,
                        InputC = 5,
                        ParameterA = 7f,
                        ParameterB = 3f,
                        ParameterC = 0.5f,
                        ParameterD = 2f
                    }
                },
                new[]
                {
                    Output("polar", 6, ControlSignalType.Vector2),
                    Output("cylindrical", 7, ControlSignalType.Vector3),
                    Output("brownian", 8, ControlSignalType.Vector3)
                },
                0,
                0);
            var executor = new ControlGraphExecutor(graph);
            var values = new[] { 0f, 2f, 3f, 1f, 0.5f, 1f };
            var frame = new ControlSignalFrame(
                profile,
                1.25,
                1f / 120f,
                ControlPlaybackDirection.Forward,
                false,
                values,
                Filled(values.Length),
                Array.Empty<float>(),
                Array.Empty<float>(),
                Array.Empty<byte>());

            executor.Evaluate(frame);
            executor.TryGetOutput(0, out Vector2 polar);
            executor.TryGetOutput(1, out Vector3 cylindrical);
            executor.TryGetOutput(2, out Vector3 firstBrownian);
            executor.Reset();
            executor.Evaluate(frame);
            executor.TryGetOutput(2, out Vector3 secondBrownian);

            Assert.That(polar, Is.EqualTo(new Vector2(2f, 0f)));
            Assert.That(cylindrical, Is.EqualTo(new Vector3(2f, 3f, 0f)));
            Assert.That(firstBrownian, Is.EqualTo(secondBrownian));
            Assert.That(firstBrownian.magnitude,
                Is.LessThanOrEqualTo(Mathf.Sqrt(3f) + 0.0001f));
        }

        [Test]
        public void Target_MergesIndependentProviderProfilesWithoutOverwriting()
        {
            var graphProfile = new ControlSignalProfile(
                "Combined",
                new[] { Descriptor("audio.value"), Descriptor("manual.value") },
                Array.Empty<ControlSignalDescriptor>());
            CompiledControlGraph graph = ScriptableObject.CreateInstance<CompiledControlGraph>();
            graph.Configure(
                graphProfile,
                new[]
                {
                    Source(0),
                    Source(1),
                    Typed(ControlOperationKind.Add, ControlSignalType.Continuous, 0, 1)
                },
                new[] { Output("combined", 2, ControlSignalType.Continuous) },
                0,
                0);
            var targetObject = new GameObject("Combined Target");
            ControlSignalTarget target = targetObject.AddComponent<ControlSignalTarget>();
            target.BindGraph(graph);

            var audioProfile = new ControlSignalProfile(
                "Audio",
                new[] { Descriptor("audio.value") },
                Array.Empty<ControlSignalDescriptor>());
            var manualProfile = new ControlSignalProfile(
                "Manual",
                new[] { Descriptor("manual.value") },
                Array.Empty<ControlSignalDescriptor>());
            target.Process(Frame(audioProfile, new[] { 2f }));
            target.Process(Frame(manualProfile, new[] { 3f }));

            Assert.That(
                target.TryGetOutput(0, out float value, out _),
                Is.True);
            Assert.That(value, Is.EqualTo(5f));
            UnityEngine.Object.DestroyImmediate(targetObject);
        }

        [Test]
        public void UrpLitAdapter_ChangesPropertyBlockWithoutMutatingSharedMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader);
            material.color = Color.white;
            var gameObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            MeshRenderer renderer = gameObject.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            UrpLitColorControlSlot adapter =
                gameObject.AddComponent<UrpLitColorControlSlot>();
            adapter.Configure(renderer);

            Color expected = new(0.2f, 0.4f, 0.8f, 1f);
            adapter.WriteColor(0, expected);
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block, 0);

            Assert.That(adapter.LastColor, Is.EqualTo(expected));
            Color rendered = block.GetColor("_BaseColor");
            Assert.That(rendered.r, Is.EqualTo(expected.r).Within(0.0001f));
            Assert.That(rendered.g, Is.EqualTo(expected.g).Within(0.0001f));
            Assert.That(rendered.b, Is.EqualTo(expected.b).Within(0.0001f));
            Assert.That(rendered.a, Is.EqualTo(expected.a).Within(0.0001f));
            Assert.That(material.color, Is.EqualTo(Color.white));

            UnityEngine.Object.DestroyImmediate(gameObject);
            UnityEngine.Object.DestroyImmediate(material);
        }

        private static ControlSignalDescriptor Descriptor(string name)
        {
            return new ControlSignalDescriptor
            {
                Name = name,
                Type = ControlSignalType.Continuous
            };
        }

        private static ControlOperation Source(int sourceIndex)
        {
            return new ControlOperation
            {
                Kind = ControlOperationKind.ContinuousSource,
                OutputType = ControlSignalType.Continuous,
                InputA = -1,
                InputB = -1,
                InputC = -1,
                InputD = -1,
                SourceIndex = sourceIndex
            };
        }

        private static ControlOperation Typed(
            ControlOperationKind kind,
            ControlSignalType outputType,
            int a,
            int b = -1,
            int c = -1,
            int d = -1)
        {
            return new ControlOperation
            {
                Kind = kind,
                OutputType = outputType,
                InputA = a,
                InputB = b,
                InputC = c,
                InputD = d,
                SourceIndex = -1
            };
        }

        private static ControlGraphOutput Output(
            string name,
            int operationIndex,
            ControlSignalType type)
        {
            return new ControlGraphOutput
            {
                Name = name,
                Id = ControlSignalId.FromString(name).Value,
                OperationIndex = operationIndex,
                Type = type
            };
        }

        private static ControlSignalFrame Frame(
            ControlSignalProfile profile,
            float[] values)
        {
            return new ControlSignalFrame(
                profile,
                0,
                1f / 60f,
                ControlPlaybackDirection.Forward,
                false,
                values,
                Filled(values.Length),
                Array.Empty<float>(),
                Array.Empty<float>(),
                Array.Empty<byte>());
        }

        private static byte[] Filled(int length)
        {
            var result = new byte[length];
            Array.Fill(result, (byte)1);
            return result;
        }
    }
}
