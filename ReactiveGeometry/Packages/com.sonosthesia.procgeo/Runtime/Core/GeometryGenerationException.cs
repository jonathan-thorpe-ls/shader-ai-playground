using System;

namespace Sonosthesia.ProcGeo
{
    public sealed class GeometryGenerationException : Exception
    {
        public GeometryGenerationException(string message)
            : base(message)
        {
        }

        public GeometryGenerationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
