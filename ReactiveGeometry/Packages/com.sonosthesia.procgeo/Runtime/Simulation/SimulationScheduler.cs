using UnityEngine;

namespace Sonosthesia.ProcGeo
{
    public sealed class SimulationScheduler
    {
        private float _accumulator;

        public int LastStepCount { get; private set; }
        public float LastDroppedTime { get; private set; }
        public float Accumulator => _accumulator;

        public int Accumulate(float elapsed, SimulationParameters parameters)
        {
            if (parameters.paused || parameters.fixedDeltaTime <= 0f)
            {
                LastStepCount = 0;
                LastDroppedTime = 0f;
                return 0;
            }

            _accumulator += Mathf.Clamp(elapsed, 0f, parameters.maximumCatchUpTime);
            int steps = 0;
            while (_accumulator + 1e-7f >= parameters.fixedDeltaTime &&
                   steps < parameters.maximumSubsteps)
            {
                _accumulator -= parameters.fixedDeltaTime;
                steps++;
            }

            float maximumRemainder = parameters.fixedDeltaTime - 1e-6f;
            LastDroppedTime = Mathf.Max(0f, _accumulator - maximumRemainder);
            if (LastDroppedTime > 0f)
                _accumulator = maximumRemainder;
            LastStepCount = steps;
            return steps;
        }

        public void Reset()
        {
            _accumulator = 0f;
            LastStepCount = 0;
            LastDroppedTime = 0f;
        }
    }
}
