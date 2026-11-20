using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sonosthesia.ProcGeo
{
    [Serializable]
    public struct SimulationParameters
    {
        [Min(1f / 120f)] public float fixedDeltaTime;
        [Range(1, 8)] public int maximumSubsteps;
        [Min(0f)] public float maximumCatchUpTime;
        [Min(0f)] public float stiffness;
        [Min(0f)] public float restoringStiffness;
        [Min(0f)] public float damping;
        [Min(0.001f)] public float maximumVelocity;
        [Min(0.001f)] public float maximumDisplacement;
        [Min(0f), Tooltip(
            "Maximum inward movement along the rest normal. Zero disables the limit.")]
        public float maximumInwardDisplacement;
        public bool paused;

        public static SimulationParameters Nominal => new()
        {
            fixedDeltaTime = 1f / 60f,
            maximumSubsteps = 4,
            maximumCatchUpTime = 0.25f,
            stiffness = 90f,
            restoringStiffness = 12f,
            damping = 2.5f,
            maximumVelocity = 12f,
            maximumDisplacement = 0.35f,
            maximumInwardDisplacement = 0.25f,
            paused = false
        };

        public static SimulationParameters Maximum => new()
        {
            fixedDeltaTime = 1f / 30f,
            maximumSubsteps = 8,
            maximumCatchUpTime = 0.25f,
            stiffness = 240f,
            restoringStiffness = 30f,
            damping = 4f,
            maximumVelocity = 30f,
            maximumDisplacement = 0.75f,
            maximumInwardDisplacement = 0.5f,
            paused = false
        };

        public static SimulationParameters Gentle => new()
        {
            fixedDeltaTime = 1f / 120f,
            maximumSubsteps = 4,
            maximumCatchUpTime = 0.1f,
            stiffness = 40f,
            restoringStiffness = 8f,
            damping = 3f,
            maximumVelocity = 6f,
            maximumDisplacement = 0.2f,
            maximumInwardDisplacement = 0.15f,
            paused = false
        };

        public static SimulationParameters Validate(
            SimulationParameters input,
            out List<string> issues)
        {
            SimulationParameters result = input;
            issues = new List<string>();
            ValidateFinite(ref result.fixedDeltaTime, 1f / 120f, 1f / 30f, nameof(fixedDeltaTime), issues);
            ValidateFinite(ref result.maximumCatchUpTime, 0f, 0.25f, nameof(maximumCatchUpTime), issues);
            ValidateInteger(ref result.maximumSubsteps, 1, 8, nameof(maximumSubsteps), issues);
            ValidateFinite(ref result.stiffness, 0f, 240f, nameof(stiffness), issues);
            ValidateFinite(ref result.restoringStiffness, 0f, 30f, nameof(restoringStiffness), issues);
            ValidateFinite(ref result.damping, 0f, 16f, nameof(damping), issues);
            ValidateFinite(ref result.maximumVelocity, 0.001f, 100f, nameof(maximumVelocity), issues);
            ValidateFinite(ref result.maximumDisplacement, 0.001f, 2f, nameof(maximumDisplacement), issues);
            ValidateFinite(
                ref result.maximumInwardDisplacement,
                0f,
                2f,
                nameof(maximumInwardDisplacement),
                issues);
            return result;
        }

        public static bool IsUninitialized(SimulationParameters value) =>
            value.fixedDeltaTime == 0f &&
            value.maximumSubsteps == 0 &&
            value.maximumCatchUpTime == 0f &&
            value.stiffness == 0f &&
            value.restoringStiffness == 0f &&
            value.damping == 0f &&
            value.maximumVelocity == 0f &&
            value.maximumDisplacement == 0f &&
            value.maximumInwardDisplacement == 0f;

        public bool IsValid()
        {
            SimulationParameters value = Validate(this, out List<string> issues);
            return issues.Count == 0 &&
                   Mathf.Approximately(value.fixedDeltaTime, fixedDeltaTime) &&
                   Mathf.Approximately(value.maximumCatchUpTime, maximumCatchUpTime) &&
                   value.maximumSubsteps == maximumSubsteps &&
                   Mathf.Approximately(value.stiffness, stiffness) &&
                   Mathf.Approximately(value.restoringStiffness, restoringStiffness) &&
                   Mathf.Approximately(value.damping, damping) &&
                   Mathf.Approximately(value.maximumVelocity, maximumVelocity) &&
                   Mathf.Approximately(
                       value.maximumDisplacement,
                       maximumDisplacement) &&
                   Mathf.Approximately(
                       value.maximumInwardDisplacement,
                       maximumInwardDisplacement);
        }

        private static void ValidateFinite(
            ref float value,
            float minimum,
            float maximum,
            string name,
            ICollection<string> issues)
        {
            float clamped = value;
            if (float.IsNaN(value) || float.IsInfinity(value))
                clamped = minimum;
            else
                clamped = Mathf.Clamp(value, minimum, maximum);
            if (!Mathf.Approximately(clamped, value))
                issues.Add($"{name} must be finite and between {minimum:g} and {maximum:g}.");
            value = clamped;
        }

        private static void ValidateInteger(
            ref int value,
            int minimum,
            int maximum,
            string name,
            ICollection<string> issues)
        {
            int clamped = Mathf.Clamp(value, minimum, maximum);
            if (clamped != value)
                issues.Add($"{name} must be between {minimum} and {maximum}.");
            value = clamped;
        }
    }
}
