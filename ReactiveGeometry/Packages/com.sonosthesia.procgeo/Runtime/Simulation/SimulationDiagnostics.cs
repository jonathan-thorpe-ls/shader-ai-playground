namespace Sonosthesia.ProcGeo
{
    public static class SimulationDiagnostics
    {
        public static SimulationStatus ReadStatus(GeometryResource resource)
        {
            if (resource == null) throw new System.ArgumentNullException(nameof(resource));
            return resource.Live.Simulation.ReadStatus();
        }
    }
}
