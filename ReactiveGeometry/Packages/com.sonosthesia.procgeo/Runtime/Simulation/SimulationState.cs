using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sonosthesia.ProcGeo
{
    public sealed class SimulationState : IDisposable
    {
        private GraphicsBuffer[] _positions;
        private GraphicsBuffer[] _velocities;
        private GraphicsBuffer _status;
        private bool _disposed;

        internal SimulationState(GeometryLayout layout, object owner)
        {
            _positions = new GraphicsBuffer[2];
            _velocities = new GraphicsBuffer[2];
            for (int index = 0; index < 2; index++)
            {
                _positions[index] = Create(
                    $"{layout.Name}.SimulationPositions{index}",
                    owner,
                    layout.VertexCount,
                    3 * sizeof(float));
                _velocities[index] = Create(
                    $"{layout.Name}.SimulationVelocities{index}",
                    owner,
                    layout.VertexCount,
                    3 * sizeof(float));
            }
            _status = Create(
                $"{layout.Name}.SimulationStatus",
                owner,
                6,
                sizeof(uint));
        }

        public GraphicsBuffer Position(int index) => _positions[index];
        public GraphicsBuffer Velocity(int index) => _velocities[index];
        public GraphicsBuffer Status => _status;

        public SimulationStatus ReadStatus()
        {
            uint[] raw = new uint[6];
            _status.GetData(raw);
            return SimulationStatus.FromRaw(raw);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            for (int index = 0; index < 2; index++)
            {
                GpuBufferRegistry.Release(ref _positions[index]);
                GpuBufferRegistry.Release(ref _velocities[index]);
            }
            GpuBufferRegistry.Release(ref _status);
        }

        private static GraphicsBuffer Create(
            string name,
            object owner,
            int count,
            int stride)
        {
            GraphicsBuffer buffer = new(GraphicsBuffer.Target.Structured, count, stride);
            GpuBufferRegistry.Register(buffer, name, owner);
            return buffer;
        }
    }

    public readonly struct SimulationStatus
    {
        public readonly uint InvalidValueCount;
        public readonly uint ClampedVelocityCount;
        public readonly uint ClampedDisplacementCount;
        public readonly float MaximumSpeed;
        public readonly float MaximumDisplacement;
        public readonly uint StepSequence;

        public SimulationStatus(
            uint invalidValueCount,
            uint clampedVelocityCount,
            uint clampedDisplacementCount,
            float maximumSpeed,
            float maximumDisplacement,
            uint stepSequence)
        {
            InvalidValueCount = invalidValueCount;
            ClampedVelocityCount = clampedVelocityCount;
            ClampedDisplacementCount = clampedDisplacementCount;
            MaximumSpeed = maximumSpeed;
            MaximumDisplacement = maximumDisplacement;
            StepSequence = stepSequence;
        }

        internal static SimulationStatus FromRaw(uint[] raw)
        {
            return new SimulationStatus(
                raw[0],
                raw[1],
                raw[2],
                UInt32BitsToSingle(raw[3]),
                UInt32BitsToSingle(raw[4]),
                raw[5]);
        }

        private static float UInt32BitsToSingle(uint value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            return BitConverter.ToSingle(bytes, 0);
        }
    }
}
