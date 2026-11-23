using System;
using NUnit.Framework;
using UnityEngine;

namespace Sonosthesia.Signals.Tests
{
    public sealed class ControlGraphExecutorTests
    {
        private const string InputName = "test.provider.value";
        private const string OutputName = "test.consumer.value";

        [Test]
        public void Executor_EvaluatesProviderNeutralOperations()
        {
            ControlSignalProfile profile = Profile();
            CompiledControlGraph graph = ScriptableObject.CreateInstance<CompiledControlGraph>();
            graph.Configure(
                profile,
                new[]
                {
                    new ControlOperation
                    {
                        Kind = ControlOperationKind.ContinuousSource,
                        InputA = -1,
                        InputB = -1,
                        SourceIndex = 0
                    },
                    new ControlOperation
                    {
                        Kind = ControlOperationKind.Remap,
                        InputA = 0,
                        InputB = -1,
                        ParameterA = 0f,
                        ParameterB = 10f,
                        ParameterC = 1f,
                        ParameterD = 3f
                    }
                },
                new[]
                {
                    new ControlGraphOutput
                    {
                        Name = OutputName,
                        Id = ControlSignalId.FromString(OutputName).Value,
                        OperationIndex = 1,
                        Type = ControlSignalType.Continuous
                    }
                },
                0,
                0);

            var executor = new ControlGraphExecutor(graph);
            var continuous = new[] { 5f };
            var available = new byte[] { 1 };
            var pulses = Array.Empty<float>();
            var pulseStarted = Array.Empty<byte>();
            var frame = new ControlSignalFrame(
                profile,
                1,
                1f / 60f,
                ControlPlaybackDirection.Forward,
                true,
                continuous,
                available,
                pulses,
                pulses,
                pulseStarted);

            executor.Evaluate(frame);

            Assert.That(executor.TryGetOutput(0, out float value, out _), Is.True);
            Assert.That(value, Is.EqualTo(2f).Within(0.0001f));
        }

        [Test]
        public void Executor_WarmEvaluationAllocatesNoManagedMemory()
        {
            ControlSignalProfile profile = Profile();
            CompiledControlGraph graph = ScriptableObject.CreateInstance<CompiledControlGraph>();
            graph.Configure(
                profile,
                new[]
                {
                    new ControlOperation
                    {
                        Kind = ControlOperationKind.ContinuousSource,
                        InputA = -1,
                        InputB = -1,
                        SourceIndex = 0
                    },
                    new ControlOperation
                    {
                        Kind = ControlOperationKind.LowPass,
                        InputA = 0,
                        InputB = -1,
                        StateOffset = 0,
                        ParameterA = 0.1f
                    }
                },
                new[]
                {
                    new ControlGraphOutput
                    {
                        Name = OutputName,
                        Id = ControlSignalId.FromString(OutputName).Value,
                        OperationIndex = 1,
                        Type = ControlSignalType.Continuous
                    }
                },
                1,
                0);
            var executor = new ControlGraphExecutor(graph);
            var continuous = new[] { 4f };
            var available = new byte[] { 1 };
            var empty = Array.Empty<float>();
            var emptyBytes = Array.Empty<byte>();
            var frame = new ControlSignalFrame(
                profile,
                0,
                1f / 120f,
                ControlPlaybackDirection.Forward,
                false,
                continuous,
                available,
                empty,
                empty,
                emptyBytes);

            executor.Evaluate(frame);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 100; index++)
                executor.Evaluate(frame);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void Target_RemapsProviderProfilesByStableId()
        {
            ControlSignalProfile graphProfile = Profile();
            CompiledControlGraph graph = ScriptableObject.CreateInstance<CompiledControlGraph>();
            graph.Configure(
                graphProfile,
                new[]
                {
                    new ControlOperation
                    {
                        Kind = ControlOperationKind.ContinuousSource,
                        InputA = -1,
                        InputB = -1,
                        SourceIndex = 0
                    }
                },
                new[]
                {
                    new ControlGraphOutput
                    {
                        Name = OutputName,
                        Id = ControlSignalId.FromString(OutputName).Value,
                        OperationIndex = 0,
                        Type = ControlSignalType.Continuous
                    }
                },
                0,
                0);

            var objectWithTarget = new GameObject("Control Target");
            ControlSignalTarget target = objectWithTarget.AddComponent<ControlSignalTarget>();
            target.BindGraph(graph);

            var providerProfile = new ControlSignalProfile(
                "Other Provider",
                new[]
                {
                    new ControlSignalDescriptor
                    {
                        Name = "other.unused",
                        Type = ControlSignalType.Continuous
                    },
                    new ControlSignalDescriptor
                    {
                        Name = InputName,
                        Type = ControlSignalType.Continuous
                    }
                },
                Array.Empty<ControlSignalDescriptor>());
            var frame = new ControlSignalFrame(
                providerProfile,
                1,
                1f / 60f,
                ControlPlaybackDirection.Forward,
                false,
                new[] { 999f, 7f },
                new byte[] { 1, 1 },
                Array.Empty<float>(),
                Array.Empty<float>(),
                Array.Empty<byte>());

            target.Process(frame);

            Assert.That(target.TryGetOutput(0, out float value, out _), Is.True);
            Assert.That(value, Is.EqualTo(7f));
            UnityEngine.Object.DestroyImmediate(objectWithTarget);
        }

        [Test]
        public void Executor_PreservesPulseStartAcrossSourceAndOutput()
        {
            var profile = new ControlSignalProfile(
                "Pulse Provider",
                Array.Empty<ControlSignalDescriptor>(),
                new[]
                {
                    new ControlSignalDescriptor
                    {
                        Name = "test.pulse",
                        Type = ControlSignalType.Pulse
                    }
                });
            CompiledControlGraph graph = ScriptableObject.CreateInstance<CompiledControlGraph>();
            graph.Configure(
                profile,
                new[]
                {
                    new ControlOperation
                    {
                        Kind = ControlOperationKind.PulseStrengthSource,
                        InputA = -1,
                        InputB = -1,
                        SourceIndex = 0
                    }
                },
                new[]
                {
                    new ControlGraphOutput
                    {
                        Name = "test.output",
                        Id = ControlSignalId.FromString("test.output").Value,
                        OperationIndex = 0,
                        Type = ControlSignalType.Pulse
                    }
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
                Array.Empty<float>(),
                Array.Empty<byte>(),
                new[] { 0.8f },
                new[] { -12f },
                new byte[] { 1 });

            executor.Evaluate(frame);

            Assert.That(executor.TryGetOutput(0, out _, out ControlPulse pulse), Is.True);
            Assert.That(pulse.Strength, Is.EqualTo(0.8f));
            Assert.That(pulse.Started, Is.True);
        }

        private static ControlSignalProfile Profile()
        {
            return new ControlSignalProfile(
                "Mock Provider",
                new[]
                {
                    new ControlSignalDescriptor
                    {
                        Name = InputName,
                        Type = ControlSignalType.Continuous
                    }
                },
                Array.Empty<ControlSignalDescriptor>());
        }
    }
}
