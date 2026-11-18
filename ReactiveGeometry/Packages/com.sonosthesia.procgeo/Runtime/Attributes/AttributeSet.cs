using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sonosthesia.ProcGeo
{
    public sealed class AttributeSet : IDisposable
    {
        private readonly Dictionary<AttributeId, Entry> _entries = new();
        private readonly object _owner;
        private bool _disposed;

        public AttributeSet(GeometryLayout layout, object owner)
        {
            _owner = owner;
            Layout = layout;
            foreach (AttributeDescriptor descriptor in layout.Attributes)
            {
                if (_entries.ContainsKey(descriptor.Id))
                    throw new ArgumentException($"Duplicate attribute '{descriptor.Id}'.", nameof(layout));

                int count = layout.AttributeCount(descriptor.Domain);
                if (count <= 0)
                    continue;

                GraphicsBuffer buffer = new(
                    GraphicsBuffer.Target.Structured,
                    count,
                    descriptor.Stride);
                GpuBufferRegistry.Register(buffer, $"{layout.Name}.{descriptor.Id}", owner);
                _entries.Add(descriptor.Id, new Entry(descriptor, buffer));
            }
        }

        public GeometryLayout Layout { get; }
        public IReadOnlyDictionary<AttributeId, Entry> Entries => _entries;

        public bool TryGet(AttributeId id, out Entry entry) => _entries.TryGetValue(id, out entry);

        public Entry Require(AttributeId id)
        {
            if (!_entries.TryGetValue(id, out Entry entry))
                throw new KeyNotFoundException($"Geometry '{Layout.Name}' does not provide attribute '{id}'.");
            return entry;
        }

        public void Bind(CommandBuffer command, AttributeId id, string shaderName)
        {
            command.SetGlobalBuffer(shaderName, Require(id).Buffer);
        }

        public void Bind(ComputeShader shader, int kernel, AttributeId id, string shaderName)
        {
            shader.SetBuffer(kernel, shaderName, Require(id).Buffer);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (Entry entry in _entries.Values)
            {
                GraphicsBuffer buffer = entry.Buffer;
                GpuBufferRegistry.Release(ref buffer);
            }
            _entries.Clear();
        }

        public sealed class Entry
        {
            internal Entry(AttributeDescriptor descriptor, GraphicsBuffer buffer)
            {
                Descriptor = descriptor;
                Buffer = buffer;
            }

            public AttributeDescriptor Descriptor { get; }
            public GraphicsBuffer Buffer { get; }
        }
    }
}
