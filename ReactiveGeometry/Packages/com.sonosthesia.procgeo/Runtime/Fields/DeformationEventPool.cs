using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sonosthesia.ProcGeo
{
    public enum DeformationEventKind
    {
        Impulse,
        Wave
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DeformationEventRecord
    {
        public uint SequenceId;
        public int Kind;
        public float CenterX;
        public float CenterY;
        public float CenterZ;
        public float StartTime;
        public float Amplitude;
        public float Radius;
        public float Width;
        public float Speed;
        public float Duration;
        public float Attenuation;
        public float Frequency;
        public float Phase;
        public int DirectionMode;
        public int Active;
        public float DirectionX;
        public float DirectionY;
        public float DirectionZ;
    }

    public sealed class DeformationEventPool : IDisposable
    {
        public const int DefaultCapacity = 32;

        private readonly DeformationEventRecord[] _records;
        private GraphicsBuffer _buffer;
        private uint _nextSequence = 1;
        private bool _disposed;

        public DeformationEventPool(
            int capacity = DefaultCapacity,
            string diagnosticName = "DeformationEvents")
        {
            if (capacity < 1 || capacity > 256)
                throw new ArgumentOutOfRangeException(
                    nameof(capacity), "Event capacity must be between 1 and 256.");
            if (string.IsNullOrWhiteSpace(diagnosticName))
                throw new ArgumentException(
                    "A diagnostic name is required.", nameof(diagnosticName));
            _records = new DeformationEventRecord[capacity];
            _buffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                capacity,
                Marshal.SizeOf<DeformationEventRecord>());
            GpuBufferRegistry.Register(_buffer, diagnosticName, this);
        }

        public int Capacity => _records.Length;
        public int ActiveCount
        {
            get
            {
                int count = 0;
                for (int index = 0; index < _records.Length; index++)
                    count += _records[index].Active;
                return count;
            }
        }
        public GraphicsBuffer Buffer => _buffer;
        public uint LastReplacementSequence { get; private set; }
        public ref readonly DeformationEventRecord RecordAt(int index)
        {
            if ((uint)index >= (uint)_records.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            return ref _records[index];
        }

        public uint AddImpulse(
            Vector3 localCenter,
            float simulationTime,
            float amplitude,
            float radius,
            DeformationDirectionMode directionMode = DeformationDirectionMode.Outward,
            Vector3 localDirection = default)
        {
            ThrowIfDisposed();
            return Add(
                DeformationEventKind.Impulse,
                localCenter,
                simulationTime,
                amplitude,
                radius,
                0f,
                0f,
                1f,
                0f,
                0f,
                0f,
                directionMode,
                localDirection);
        }

        public uint AddWave(
            Vector3 localCenter,
            float simulationTime,
            float amplitude,
            float speed,
            float width,
            float duration,
            float attenuation = 0f,
            float frequency = 0f,
            float phase = 0f,
            DeformationDirectionMode directionMode = DeformationDirectionMode.Outward,
            Vector3 localDirection = default)
        {
            ThrowIfDisposed();
            return Add(
                DeformationEventKind.Wave,
                localCenter,
                simulationTime,
                amplitude,
                0f,
                width,
                speed,
                duration,
                attenuation,
                frequency,
                phase,
                directionMode,
                localDirection);
        }

        public void Prepare(float simulationTime)
        {
            ThrowIfDisposed();
            for (int index = 0; index < _records.Length; index++)
            {
                DeformationEventRecord record = _records[index];
                if (record.Active == 0 || record.Kind != (int)DeformationEventKind.Wave)
                    continue;
                if (simulationTime - record.StartTime > record.Duration)
                {
                    record.Active = 0;
                    _records[index] = record;
                }
            }
            _buffer.SetData(_records);
        }

        public void Reset()
        {
            ThrowIfDisposed();
            Array.Clear(_records, 0, _records.Length);
            _buffer.SetData(_records);
        }

        public bool IsActive(uint sequenceId)
        {
            for (int index = 0; index < _records.Length; index++)
                if (_records[index].SequenceId == sequenceId)
                    return _records[index].Active != 0;
            return false;
        }

        internal void ConsumeImpulses()
        {
            for (int index = 0; index < _records.Length; index++)
            {
                DeformationEventRecord record = _records[index];
                if (record.Active != 0 && record.Kind == (int)DeformationEventKind.Impulse)
                {
                    record.Active = 0;
                    _records[index] = record;
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            GpuBufferRegistry.Release(ref _buffer);
        }

        private uint Add(
            DeformationEventKind kind,
            Vector3 center,
            float startTime,
            float amplitude,
            float radius,
            float width,
            float speed,
            float duration,
            float attenuation,
            float frequency,
            float phase,
            DeformationDirectionMode directionMode,
            Vector3 direction)
        {
            int slot = -1;
            for (int index = 0; index < _records.Length; index++)
            {
                if (_records[index].Active == 0)
                {
                    slot = index;
                    break;
                }
            }
            if (slot < 0)
            {
                uint oldestSequence = uint.MaxValue;
                for (int index = 0; index < _records.Length; index++)
                {
                    if (_records[index].SequenceId < oldestSequence)
                    {
                        oldestSequence = _records[index].SequenceId;
                        slot = index;
                    }
                }
                LastReplacementSequence = _records[slot].SequenceId;
                Debug.LogWarning(
                    $"Deformation event pool is full; replacing event " +
                    $"{LastReplacementSequence}.");
            }

            center = SpatialDeformationValidation.FiniteVector(center, Vector3.zero);
            direction = SpatialDeformationValidation.FiniteVector(direction, Vector3.forward);
            if (direction.sqrMagnitude > 1e-8f)
                direction.Normalize();
            else
                direction = Vector3.forward;

            uint sequence = _nextSequence++;
            if (sequence == 0) sequence = _nextSequence++;
            _records[slot] = new DeformationEventRecord
            {
                SequenceId = sequence,
                Kind = (int)kind,
                CenterX = center.x,
                CenterY = center.y,
                CenterZ = center.z,
                StartTime = SpatialDeformationValidation.Finite(startTime, 0f),
                Amplitude = SpatialDeformationValidation.Finite(amplitude, 0f),
                Radius = Mathf.Max(SpatialDeformationValidation.Finite(radius, 0f), 0f),
                Width = Mathf.Max(SpatialDeformationValidation.Finite(width, 0.001f), 0.001f),
                Speed = Mathf.Max(SpatialDeformationValidation.Finite(speed, 0f), 0f),
                Duration = Mathf.Max(SpatialDeformationValidation.Finite(duration, 0f), 0f),
                Attenuation = Mathf.Max(
                    SpatialDeformationValidation.Finite(attenuation, 0f), 0f),
                Frequency = SpatialDeformationValidation.Finite(frequency, 0f),
                Phase = SpatialDeformationValidation.Finite(phase, 0f),
                DirectionMode = (int)directionMode,
                Active = 1,
                DirectionX = direction.x,
                DirectionY = direction.y,
                DirectionZ = direction.z
            };
            return sequence;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DeformationEventPool));
        }
    }

    public sealed class ImpulseDeformationField : DeformationEventFieldBase
    {
        public ImpulseDeformationField(
            DeformationEventPool pool,
            ComputeShader shader = null)
            : base("SpatialImpulse", DeformationEventKind.Impulse, pool, shader)
        {
        }
    }

    public sealed class RadialWaveDeformationField : DeformationEventFieldBase
    {
        public RadialWaveDeformationField(
            DeformationEventPool pool,
            ComputeShader shader = null)
            : base("RadialWave", DeformationEventKind.Wave, pool, shader)
        {
        }
    }

    public abstract class DeformationEventFieldBase : IGeometryField
    {
        private readonly DeformationEventPool _pool;
        private readonly ComputeShader _shader;
        private readonly int _kernel;
        private readonly DeformationEventKind _kind;
        private readonly string _name;

        protected DeformationEventFieldBase(
            string name,
            DeformationEventKind kind,
            DeformationEventPool pool,
            ComputeShader shader)
        {
            _name = name;
            _kind = kind;
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _shader = shader ?? Resources.Load<ComputeShader>("ProcGeo/EventFields")
                ?? throw new InvalidOperationException(
                    "ProcGeo/EventFields.compute was not found.");
            _kernel = _shader.FindKernel("EventForce");
        }

        public string Name => _name;
        public GeometryFieldContribution Contribution =>
            GeometryFieldContribution.Force;
        public IReadOnlyList<AttributeDependency> RequiredAttributes { get; } =
            new[]
            {
                new AttributeDependency(
                    AttributeIds.RestPosition,
                    AttributeDomain.Vertex,
                    AttributeFormat.Float3),
                new AttributeDependency(
                    AttributeIds.RestNormal,
                    AttributeDomain.Vertex,
                    AttributeFormat.Float3)
            };

        public void Validate(GeometryResource resource)
        {
            SpatialDeformationValidation.ValidateRequired(
                resource, RequiredAttributes, Name);
            SpatialDeformationValidation.ValidateOptionalWeights(resource, Name);
        }

        public void Record(
            CommandBuffer command,
            GeometryResource resource,
            GraphicsBuffer contribution,
            FieldParameters parameters)
        {
            _pool.Prepare(parameters.simulationTime);
            command.SetComputeIntParam(
                _shader, "_VertexCount", resource.Layout.VertexCount);
            command.SetComputeIntParam(_shader, "_EventCapacity", _pool.Capacity);
            command.SetComputeIntParam(_shader, "_EventKind", (int)_kind);
            command.SetComputeFloatParam(
                _shader, "_SimulationTime", parameters.simulationTime);
            command.SetComputeBufferParam(
                _shader, _kernel, "_Events", _pool.Buffer);
            command.SetComputeBufferParam(
                _shader, _kernel, "_RestPositions",
                resource.Attributes.Require(AttributeIds.RestPosition).Buffer);
            command.SetComputeBufferParam(
                _shader, _kernel, "_RestNormals",
                resource.Attributes.Require(AttributeIds.RestNormal).Buffer);
            SpatialDeformationValidation.BindWeight(
                command, _shader, _kernel, resource);
            command.SetComputeBufferParam(
                _shader, _kernel, "_ForceBuffer", contribution);
            command.DispatchCompute(
                _shader, _kernel,
                (resource.Layout.VertexCount + 63) / 64, 1, 1);
            if (_kind == DeformationEventKind.Impulse)
                _pool.ConsumeImpulses();
        }
    }
}
