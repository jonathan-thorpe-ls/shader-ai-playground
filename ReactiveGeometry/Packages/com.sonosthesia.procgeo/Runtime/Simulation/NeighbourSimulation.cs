using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sonosthesia.ProcGeo
{
    public sealed class NeighbourSimulation : IDisposable, ISimulationStep
    {
        private readonly GeometryResource _resource;
        private readonly ComputeShader _shader;
        private readonly GeometryCommandScheduler _scheduler;
        private readonly int _initializeKernel;
        private readonly int _impulseKernel;
        private readonly int _integrateKernel;
        private readonly int _publishKernel;
        private bool _disposed;

        public NeighbourSimulation(
            GeometryResource resource,
            ComputeShader shader = null)
        {
            _resource = resource ?? throw new ArgumentNullException(nameof(resource));
            _shader = shader != null
                ? shader
                : Resources.Load<ComputeShader>("ProcGeo/NeighbourSimulation")
                    ?? throw new InvalidOperationException(
                        "ProcGeo/NeighbourSimulation.compute was not found.");
            _scheduler = new GeometryCommandScheduler($"{resource.Layout.Name} Simulation");
            _initializeKernel = _shader.FindKernel("InitializeState");
            _impulseKernel = _shader.FindKernel("ApplyImpulse");
            _integrateKernel = _shader.FindKernel("NeighbourIntegrate");
            _publishKernel = _shader.FindKernel("PublishDynamicMesh");
            Reset();
        }

        public int ReadIndex { get; private set; }
        public uint StepSequence { get; private set; }

        public void ApplyImpulse(int vertex, Vector3 direction, float magnitude)
        {
            ThrowIfDisposed();
            if ((uint)vertex >= (uint)_resource.Layout.VertexCount)
                throw new ArgumentOutOfRangeException(nameof(vertex));
            if (!IsFinite(direction) || float.IsNaN(magnitude) ||
                float.IsInfinity(magnitude) || magnitude < 0f)
                throw new ArgumentException("Impulse direction and magnitude must be finite.");

            Vector3 normalized = direction.sqrMagnitude > 1e-12f
                ? direction.normalized
                : throw new ArgumentException("Impulse direction cannot be zero.", nameof(direction));
            int read = ReadIndex;
            _scheduler.Record(command =>
            {
                command.SetComputeIntParam(_shader, "_VertexCount", _resource.Layout.VertexCount);
                command.SetComputeIntParam(_shader, "_ImpulseVertex", vertex);
                command.SetComputeVectorParam(_shader, "_ImpulseDirection", normalized);
                command.SetComputeFloatParam(_shader, "_ImpulseMagnitude", magnitude);
                command.SetComputeBufferParam(
                    _shader, _impulseKernel, "_CurrentVelocities",
                    _resource.Live.Simulation.Velocity(read));
                command.DispatchCompute(_shader, _impulseKernel, 1, 1, 1);
            });
            _scheduler.ExecuteOnce();
        }

        public void Reset()
        {
            ThrowIfDisposed();
            _scheduler.Cancel();
            _scheduler.Record(command =>
            {
                BindCommon(_initializeKernel, command);
                command.SetComputeBufferParam(
                    _shader, _initializeKernel, "_PositionsA",
                    _resource.Live.Simulation.Position(0));
                command.SetComputeBufferParam(
                    _shader, _initializeKernel, "_PositionsB",
                    _resource.Live.Simulation.Position(1));
                command.SetComputeBufferParam(
                    _shader, _initializeKernel, "_VelocitiesA",
                    _resource.Live.Simulation.Velocity(0));
                command.SetComputeBufferParam(
                    _shader, _initializeKernel, "_VelocitiesB",
                    _resource.Live.Simulation.Velocity(1));
                command.SetComputeBufferParam(
                    _shader, _initializeKernel, "_PreviousPositions",
                    _resource.Live.PreviousPositions);
                command.SetComputeBufferParam(
                    _shader, _initializeKernel, "_ExternalForces",
                    _resource.Live.Simulation.ExternalForces);
                command.SetComputeBufferParam(
                    _shader, _initializeKernel, "_SimulationStatus",
                    _resource.Live.Simulation.Status);
                command.DispatchCompute(
                    _shader, _initializeKernel, ThreadGroups(_resource.Layout.VertexCount), 1, 1);
                BindPublish(command);
                DispatchPublish(command);
            });
            _scheduler.ExecuteOnce();
            ReadIndex = 0;
            StepSequence = 0;
        }

        public int Step(float fixedDeltaTime, SimulationParameters parameters)
        {
            ThrowIfDisposed();
            SimulationParameters validated = SimulationParameters.Validate(
                parameters, out var issues);
            if (issues.Count != 0)
                throw new ArgumentException(string.Join(" ", issues), nameof(parameters));
            if (!Mathf.Approximately(fixedDeltaTime, validated.fixedDeltaTime))
                throw new ArgumentOutOfRangeException(
                    nameof(fixedDeltaTime),
                    "The timestep must match the validated simulation parameters.");
            RecordStep(validated);
            _scheduler.ExecuteOnce();
            return ReadIndex;
        }

        public void Publish()
        {
            ThrowIfDisposed();
            _scheduler.Record(command =>
            {
                BindPublish(command);
                DispatchPublish(command);
            });
            _scheduler.ExecuteOnce();
        }

        public int RecordFixedSteps(
            int count,
            SimulationParameters parameters,
            bool publish = true)
        {
            ThrowIfDisposed();
            SimulationParameters validated = SimulationParameters.Validate(
                parameters, out var issues);
            if (issues.Count != 0)
                throw new ArgumentException(string.Join(" ", issues), nameof(parameters));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            for (int index = 0; index < count; index++)
                RecordStep(validated);
            if (publish)
                _scheduler.Record(command =>
                {
                    BindPublish(command);
                    DispatchPublish(command);
                });
            _scheduler.ExecuteOnce();
            return ReadIndex;
        }

        private void RecordStep(SimulationParameters parameters)
        {
            int read = ReadIndex;
            int write = 1 - read;
            _scheduler.Record(command =>
            {
                command.SetComputeIntParam(_shader, "_VertexCount", _resource.Layout.VertexCount);
                command.SetComputeFloatParam(_shader, "_DeltaTime", parameters.fixedDeltaTime);
                command.SetComputeFloatParam(_shader, "_Epsilon", 1e-7f);
                command.SetComputeFloatParam(_shader, "_Stiffness", parameters.stiffness);
                command.SetComputeFloatParam(
                    _shader, "_RestoringStiffness", parameters.restoringStiffness);
                command.SetComputeFloatParam(_shader, "_Damping", parameters.damping);
                command.SetComputeFloatParam(_shader, "_MaximumVelocity", parameters.maximumVelocity);
                command.SetComputeFloatParam(
                    _shader, "_MaximumDisplacement", parameters.maximumDisplacement);
                command.SetComputeFloatParam(
                    _shader,
                    "_MaximumInwardDisplacement",
                    parameters.maximumInwardDisplacement);
                command.SetComputeBufferParam(
                    _shader, _integrateKernel, "_ReadPositions",
                    _resource.Live.Simulation.Position(read));
                command.SetComputeBufferParam(
                    _shader, _integrateKernel, "_ReadVelocities",
                    _resource.Live.Simulation.Velocity(read));
                command.SetComputeBufferParam(
                    _shader, _integrateKernel, "_WritePositions",
                    _resource.Live.Simulation.Position(write));
                command.SetComputeBufferParam(
                    _shader, _integrateKernel, "_WriteVelocities",
                    _resource.Live.Simulation.Velocity(write));
                command.SetComputeBufferParam(
                    _shader, _integrateKernel, "_ExternalForces",
                    _resource.Live.Simulation.ExternalForces);
                command.SetComputeBufferParam(
                    _shader, _integrateKernel, "_SimulationStatus",
                    _resource.Live.Simulation.Status);
                BindAttribute(
                    command, _integrateKernel, "_RestPositions", AttributeIds.RestPosition);
                BindAttribute(
                    command, _integrateKernel, "_RestNormals", AttributeIds.RestNormal);
                BindAttribute(command, _integrateKernel, "_Masses", AttributeIds.Mass);
                BindTopology(command, _integrateKernel);
                command.BeginSample("ProcGeo.Simulation.Step");
                command.DispatchCompute(
                    _shader, _integrateKernel, ThreadGroups(_resource.Layout.VertexCount), 1, 1);
                command.EndSample("ProcGeo.Simulation.Step");
            });
            ReadIndex = write;
            StepSequence++;
        }

        private void BindCommon(int kernel, CommandBuffer command)
        {
            command.SetComputeIntParam(_shader, "_VertexCount", _resource.Layout.VertexCount);
            BindAttribute(command, kernel, "_RestPositions", AttributeIds.RestPosition);
        }

        private void BindPublish(CommandBuffer command)
        {
            command.SetComputeIntParam(_shader, "_VertexCount", _resource.Layout.VertexCount);
            command.SetComputeFloatParam(_shader, "_Epsilon", 1e-6f);
            command.SetComputeBufferParam(
                _shader, _publishKernel, "_ReadPositions",
                _resource.Live.Simulation.Position(ReadIndex));
            command.SetComputeBufferParam(
                _shader, _publishKernel, "_DynamicVertices", _resource.Live.DynamicVertices);
            BindAttribute(command, _publishKernel, "_RestPositions", AttributeIds.RestPosition);
            command.SetComputeBufferParam(
                _shader, _publishKernel, "_Indices", _resource.Indices);
            command.SetComputeBufferParam(
                _shader, _publishKernel, "_VertexFaceOffsets",
                _resource.Topology.VertexFaceOffsets);
            command.SetComputeBufferParam(
                _shader, _publishKernel, "_VertexFaces", _resource.Topology.VertexFaces);
            BindAttribute(command, _publishKernel, "_RestNormals", AttributeIds.RestNormal);
        }

        private void DispatchPublish(CommandBuffer command) =>
            DispatchPublishInternal(command);

        private void DispatchPublishInternal(CommandBuffer command)
        {
            command.BeginSample("ProcGeo.Simulation.Normals");
            command.BeginSample("ProcGeo.Simulation.Publish");
            command.DispatchCompute(
                _shader, _publishKernel, ThreadGroups(_resource.Layout.VertexCount), 1, 1);
            command.EndSample("ProcGeo.Simulation.Publish");
            command.EndSample("ProcGeo.Simulation.Normals");
        }

        private void BindTopology(CommandBuffer command, int kernel)
        {
            command.SetComputeBufferParam(
                _shader, kernel, "_NeighbourOffsets", _resource.Topology.NeighbourOffsets);
            command.SetComputeBufferParam(
                _shader, kernel, "_Neighbours", _resource.Topology.Neighbours);
            command.SetComputeBufferParam(
                _shader, kernel, "_NeighbourEdge", _resource.Topology.NeighbourEdge);
            command.SetComputeBufferParam(
                _shader, kernel, "_RestLengths", _resource.Topology.EdgeRestLength);
            command.SetComputeBufferParam(
                _shader, kernel, "_EdgeTypes", _resource.Topology.EdgeTypes);
        }

        private void BindAttribute(
            CommandBuffer command,
            int kernel,
            string name,
            AttributeId id) =>
            command.SetComputeBufferParam(
                _shader, kernel, name, _resource.Attributes.Require(id).Buffer);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _scheduler.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(NeighbourSimulation));
        }

        private static int ThreadGroups(int count) => (count + 63) / 64;

        private static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}
