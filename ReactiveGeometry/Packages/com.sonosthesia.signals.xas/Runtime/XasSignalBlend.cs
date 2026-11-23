using System;

namespace Sonosthesia.Signals.Xas
{
    public static class XasSignalBlend
    {
        public static void ResetContinuous(
            float[] sums,
            float[] weights,
            byte[] available)
        {
            Array.Clear(sums, 0, sums.Length);
            Array.Clear(weights, 0, weights.Length);
            Array.Clear(available, 0, available.Length);
        }

        public static void AddContinuous(
            float[] input,
            byte[] inputAvailable,
            float weight,
            float[] sums,
            float[] weights,
            int count)
        {
            if (weight <= 0f)
                return;
            for (int index = 0; index < count; index++)
            {
                if (inputAvailable[index] == 0)
                    continue;
                sums[index] += input[index] * weight;
                weights[index] += weight;
            }
        }

        public static void FinishContinuous(
            float[] sums,
            float[] weights,
            byte[] available,
            int count)
        {
            for (int index = 0; index < count; index++)
            {
                if (weights[index] <= 0f)
                    continue;
                sums[index] /= weights[index];
                available[index] = 1;
            }
        }

        public static void AddPulses(
            float[] strengthInput,
            float[] magnitudeInput,
            byte[] startedInput,
            float weight,
            float[] strengths,
            float[] magnitudes,
            byte[] started,
            int count)
        {
            if (weight <= 0f)
                return;
            for (int index = 0; index < count; index++)
            {
                float strength = strengthInput[index] * weight;
                if (strength > strengths[index])
                {
                    strengths[index] = strength;
                    magnitudes[index] = magnitudeInput[index];
                }
                if (startedInput[index] != 0)
                    started[index] = 1;
            }
        }
    }
}
