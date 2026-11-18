using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sonosthesia.ProcGeo
{
    public readonly struct GpuBufferRegistration
    {
        public GpuBufferRegistration(string name, object owner, int count, int stride, bool ownsBuffer)
        {
            Name = name;
            Owner = owner;
            Count = count;
            Stride = stride;
            OwnsBuffer = ownsBuffer;
        }

        public string Name { get; }
        public object Owner { get; }
        public int Count { get; }
        public int Stride { get; }
        public long Bytes => (long)Count * Stride;
        public bool OwnsBuffer { get; }
    }

    public static class GpuBufferRegistry
    {
        private static readonly object Sync = new();
        private static readonly Dictionary<GraphicsBuffer, GpuBufferRegistration> Buffers = new();

        public static int Count
        {
            get
            {
                lock (Sync) return Buffers.Count;
            }
        }

        public static IReadOnlyCollection<GpuBufferRegistration> Snapshot
        {
            get
            {
                lock (Sync) return new List<GpuBufferRegistration>(Buffers.Values);
            }
        }

        public static void Register(
            GraphicsBuffer buffer,
            string name,
            object owner,
            bool ownsBuffer = true)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            lock (Sync)
            {
                Buffers[buffer] = new GpuBufferRegistration(
                    name,
                    owner,
                    buffer.count,
                    buffer.stride,
                    ownsBuffer);
            }
        }

        public static bool Unregister(GraphicsBuffer buffer)
        {
            if (buffer == null) return false;
            lock (Sync) return Buffers.Remove(buffer);
        }

        public static void Release(ref GraphicsBuffer buffer)
        {
            if (buffer == null) return;
            Unregister(buffer);
            buffer.Release();
            buffer = null;
        }
    }
}
