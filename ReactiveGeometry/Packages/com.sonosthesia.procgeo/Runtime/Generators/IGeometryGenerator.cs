using System;
using UnityEngine.Rendering;

namespace Sonosthesia.ProcGeo
{
    public interface IGeometryGenerator : IDisposable
    {
        GeometryLayout Plan();
        void Record(CommandBuffer command, GeometryResource target);
    }
}
