namespace Sonosthesia.ProcGeo
{
    public interface ISimulationStep
    {
        int ReadIndex { get; }
        void ApplyImpulse(int vertex, UnityEngine.Vector3 direction, float magnitude);
        void Reset();
        int Step(float fixedDeltaTime, SimulationParameters parameters);
        void Publish();
    }
}
