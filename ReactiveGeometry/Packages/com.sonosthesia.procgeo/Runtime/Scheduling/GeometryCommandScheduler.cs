using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sonosthesia.ProcGeo
{
    public sealed class GeometryCommandScheduler : IDisposable
    {
        private readonly CommandBuffer _command;
        private bool _disposed;

        public GeometryCommandScheduler(string name)
        {
            _command = new CommandBuffer { name = name };
        }

        public bool HasCommands { get; private set; }

        public CommandBuffer Command
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(nameof(GeometryCommandScheduler));
                return _command;
            }
        }

        public void ExecuteOnce()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(GeometryCommandScheduler));
            if (!HasCommands) return;
            Graphics.ExecuteCommandBuffer(_command);
            _command.Clear();
            HasCommands = false;
        }

        public void Record(Action<CommandBuffer> record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            record(Command);
            HasCommands = true;
        }

        public void Cancel()
        {
            if (_disposed) return;
            _command.Clear();
            HasCommands = false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _command.Release();
        }
    }
}
