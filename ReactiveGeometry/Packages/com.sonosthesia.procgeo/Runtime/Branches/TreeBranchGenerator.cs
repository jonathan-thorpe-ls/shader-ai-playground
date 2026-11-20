using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sonosthesia.ProcGeo
{
    public sealed class TreeBranchGenerator : IBranchGraphGenerator
    {
        private const uint NoParent = uint.MaxValue;

        public BranchGraphPlan Plan(BranchGenerationSettings settings)
        {
            settings = Validate(settings);
            System.Random random = new(settings.seed);
            List<MutableBranch> pending = new();
            List<MutableBranch> branches = new();
            int maximumDepth = 0;
            int totalSamples = 0;
            Vector3 trunkDirection = Vector3.up;
            pending.Add(CreateBranch(
                0u, NoParent, NoParent, 0, settings.initialLength,
                settings.initialRadius, Vector3.zero, trunkDirection,
                0f, settings, ref random, ref totalSamples));

            while (pending.Count > 0)
            {
                int lowestDepth = int.MaxValue;
                int pendingIndex = -1;
                for (int i = 0; i < pending.Count; i++)
                {
                    if (pending[i].Depth < lowestDepth)
                    {
                        lowestDepth = pending[i].Depth;
                        pendingIndex = i;
                    }
                }

                MutableBranch branch = pending[pendingIndex];
                pending.RemoveAt(pendingIndex);
                branches.Add(branch);
                maximumDepth = Mathf.Max(maximumDepth, branch.Depth);
                branch.Id = (uint)branches.Count - 1u;
                if (branch.Depth >= settings.maximumDepth)
                    continue;

                if (branches.Count + pending.Count +
                    settings.branchesPerNode > settings.maximumBranches)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(settings),
                        "The branch generation settings exceed the maximum branch count.");
                }

                Vector3 tip = branch.Positions[branch.Positions.Length - 1];
                Vector3 tangent = branch.Tangents[branch.Tangents.Length - 1];
                BuildFrame(tangent, out Vector3 normal, out _);
                uint parentId = branch.Id;
                for (int child = 0; child < settings.branchesPerNode; child++)
                {
                    float side = settings.branchesPerNode == 1
                        ? 0f
                        : Mathf.Lerp(-1f, 1f,
                            child / (float)(settings.branchesPerNode - 1));
                    float azimuth = child * settings.azimuthSpread *
                        Mathf.Deg2Rad / Mathf.Max(1, settings.branchesPerNode - 1);
                    Quaternion aroundParent =
                        Quaternion.AngleAxis(azimuth * Mathf.Rad2Deg, tangent);
                    Vector3 lateral = aroundParent * normal;
                    lateral = (lateral * side + normal * (1f - Mathf.Abs(side)) * 0.35f)
                        .normalized;
                    Vector3 childDirection = Vector3.Slerp(
                        tangent,
                        Vector3.ProjectOnPlane(lateral, tangent).normalized,
                        settings.branchAngle / 90f).normalized;
                    childDirection = (
                        childDirection + Vector3.up * settings.upwardBias).normalized;
                    float length = settings.initialLength *
                        Mathf.Pow(settings.lengthScale, branch.Depth + 1);
                    length *= 1f + Mathf.Lerp(
                        -settings.lengthVariation,
                        settings.lengthVariation,
                        (float)random.NextDouble());
                    float radius = settings.initialRadius *
                        Mathf.Pow(settings.radiusScale, branch.Depth + 1);
                    radius *= 1f + Mathf.Lerp(
                        -settings.radiusVariation,
                        settings.radiusVariation,
                        (float)random.NextDouble());
                    pending.Add(CreateBranch(
                        0u, parentId, NoParent, branch.Depth + 1,
                        length, radius, tip, childDirection,
                        (float)random.NextDouble() * Mathf.PI * 2f,
                        settings, ref random, ref totalSamples));
                }
            }

            Dictionary<uint, int> branchById = new();
            for (int i = 0; i < branches.Count; i++)
                branchById.Add(branches[i].Id, i);
            List<BranchRecord> records = new(branches.Count);
            List<BranchPathHeader> headers = new(branches.Count);
            List<BranchSample> samples = new(totalSamples);
            uint sampleOffset = 0;
            for (int branchIndex = 0; branchIndex < branches.Count; branchIndex++)
            {
                MutableBranch branch = branches[branchIndex];
                uint parentSample = NoParent;
                float parentDistance = 0f;
                if (branch.ParentId != NoParent)
                {
                    MutableBranch parent = branches[branchById[branch.ParentId]];
                    parentSample = parent.SampleOffset + (uint)(parent.SampleCount - 1);
                    parentDistance = parent.DistanceFromRoot;
                }
                branch.SampleOffset = sampleOffset;
                branch.DistanceFromRoot = parentDistance;
                branch.StartDistanceFromRoot = parentDistance;
                branch.Terminal = branch.Depth >= settings.maximumDepth;
                BranchRecord record = new(
                    branch.Id,
                    branch.ParentId,
                    parentSample,
                    branch.SampleOffset,
                    (uint)branch.SampleCount,
                    (uint)branch.Depth,
                    branch.RestLength,
                    branch.InitialRadius,
                    parentDistance,
                    branch.Terminal);
                records.Add(record);
                for (int sample = 0; sample < branch.SampleCount; sample++)
                {
                    Vector3 position = branch.Positions[sample];
                    float localDistance = branch.LocalDistances[sample];
                    float localPosition = branch.RestLength > 1e-6f
                        ? localDistance / branch.RestLength
                        : 0f;
                    float distanceFromRoot =
                        branch.StartDistanceFromRoot + localDistance;
                    float distanceToTip = branch.RestLength - localDistance;
                    float radius = Mathf.Lerp(
                        branch.InitialRadius,
                        branch.InitialRadius * 0.72f,
                        localPosition);
                    float tip = SmoothStep(
                        settings.tipInfluenceStart, 1f, localPosition);
                    float flexibility = settings.baseFlexibility * tip;
                    float stiffness = 1f - flexibility;
                    samples.Add(new BranchSample(
                        position,
                        branch.Tangents[sample],
                        branch.Normals[sample],
                        branch.Binormals[sample],
                        localDistance,
                        localPosition,
                        distanceFromRoot,
                        distanceToTip,
                        radius,
                        Mathf.Clamp01(flexibility),
                        Mathf.Clamp01(stiffness),
                        tip,
                        branch.Id,
                        (uint)branch.Depth));
                }
                branch.DistanceFromRoot =
                    branch.StartDistanceFromRoot + branch.RestLength;
                sampleOffset += (uint)branch.SampleCount;
                headers.Add(new BranchPathHeaderBuilder(
                    record).Build());
            }

            BranchSample[] sampleArray = samples.ToArray();
            UpdateDistanceToTip(records, sampleArray);
            float smallestSupportedRadius = settings.initialRadius *
                Mathf.Pow(settings.radiusScale, settings.maximumDepth) *
                0.72f * Mathf.Max(0.001f, 1f - settings.radiusVariation);
            for (int i = 0; i < sampleArray.Length; i++)
            {
                BranchSample sample = sampleArray[i];
                float radiusInfluence = Mathf.Pow(
                    Mathf.Clamp01(smallestSupportedRadius /
                        Mathf.Max(sample.Radius, 0.0001f)),
                    settings.radiusExponent);
                float depthInfluence = 1f -
                    Mathf.Exp(-settings.depthResponse * sample.GenerationDepth);
                float flexibility = settings.baseFlexibility *
                    sample.TipWeight *
                    (0.5f * depthInfluence + 0.5f * radiusInfluence);
                if (sample.LocalPosition <= 0f)
                    flexibility = 0f;
                sampleArray[i] = sample.WithFlexibility(
                    Mathf.Clamp01(flexibility),
                    Mathf.Clamp01(1f - flexibility));
            }
            Bounds bounds = new();
            bool hasBounds = false;
            foreach (BranchSample sample in sampleArray)
            {
                if (!hasBounds)
                {
                    bounds = new Bounds(sample.Position, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(sample.Position + Vector3.one * sample.Radius);
                    bounds.Encapsulate(sample.Position - Vector3.one * sample.Radius);
                }
            }

            return new BranchGraphPlan(
                records.ToArray(),
                sampleArray,
                headers.ToArray(),
                maximumDepth,
                bounds);
        }

        private static MutableBranch CreateBranch(
            uint id,
            uint parentId,
            uint parentSample,
            int depth,
            float length,
            float radius,
            Vector3 start,
            Vector3 direction,
            float phase,
            BranchGenerationSettings settings,
            ref System.Random random,
            ref int totalSamples)
        {
            int sampleCount = Mathf.Clamp(
                Mathf.CeilToInt(length * settings.samplesPerUnitLength),
                settings.minimumSamples,
                settings.maximumSamplesPerBranch);
            if (totalSamples + sampleCount > settings.maximumPathSamples)
                throw new ArgumentOutOfRangeException(
                    nameof(settings),
                    "The branch generation settings exceed the maximum path sample count.");
            totalSamples += sampleCount;
            Vector3[] positions = new Vector3[sampleCount];
            Vector3[] tangents = new Vector3[sampleCount];
            Vector3[] normals = new Vector3[sampleCount];
            Vector3[] binormals = new Vector3[sampleCount];
            float[] localDistances = new float[sampleCount];
            BuildFrame(direction, out Vector3 frameNormal, out Vector3 frameBinormal);
            float bend = Mathf.Lerp(
                -0.08f, 0.08f, (float)random.NextDouble()) * length;
            for (int i = 0; i < sampleCount; i++)
            {
                float t = sampleCount > 1 ? i / (float)(sampleCount - 1) : 0f;
                float curve = Mathf.Sin(t * Mathf.PI) *
                    Mathf.Sin(phase) * bend;
                Vector3 centre = start + direction * (length * t) +
                    frameNormal * curve;
                positions[i] = centre;
                localDistances[i] = length * t;
            }
            for (int i = 0; i < sampleCount; i++)
            {
                int previous = Mathf.Max(0, i - 1);
                int next = Mathf.Min(sampleCount - 1, i + 1);
                Vector3 tangent = positions[next] - positions[previous];
                tangents[i] = tangent.sqrMagnitude > 1e-10f
                    ? tangent.normalized
                    : direction.normalized;
                if (i == 0)
                {
                    normals[0] = (frameNormal -
                        tangents[0] * Vector3.Dot(frameNormal, tangents[0])).normalized;
                }
                else
                {
                    normals[i] = (normals[i - 1] -
                        tangents[i] * Vector3.Dot(normals[i - 1], tangents[i])).normalized;
                }
                if (normals[i].sqrMagnitude < 1e-8f)
                    normals[i] = frameNormal;
                binormals[i] = Vector3.Cross(tangents[i], normals[i]).normalized;
            }
            return new MutableBranch
            {
                Id = id,
                ParentId = parentId,
                ParentSample = parentSample,
                Depth = depth,
                InitialRadius = radius,
                RestLength = length,
                Positions = positions,
                Tangents = tangents,
                Normals = normals,
                Binormals = binormals,
                LocalDistances = localDistances,
                ChildIds = new List<uint>(),
                SampleCount = sampleCount
            };
        }

        private static void UpdateDistanceToTip(
            List<BranchRecord> records,
            BranchSample[] samples)
        {
            float[] subtreeRemaining = new float[records.Count];
            for (int i = records.Count - 1; i >= 0; i--)
            {
                BranchRecord branch = records[i];
                float best = branch.RestLength;
                subtreeRemaining[i] = best;
                for (int child = i + 1; child < records.Count; child++)
                {
                    BranchRecord candidate = records[child];
                    if (candidate.ParentId != branch.Id)
                        continue;
                    best = Mathf.Min(best, subtreeRemaining[child] + branch.RestLength);
                }
                subtreeRemaining[i] = best;
            }
            for (int branchIndex = 0; branchIndex < records.Count; branchIndex++)
            {
                BranchRecord branch = records[branchIndex];
                float remainingChild = float.MaxValue;
                bool hasChild = false;
                for (int child = branchIndex + 1; child < records.Count; child++)
                {
                    if (records[child].ParentId != branch.Id)
                        continue;
                    remainingChild = Mathf.Min(remainingChild, subtreeRemaining[child]);
                    hasChild = true;
                }
                for (uint i = 0; i < branch.SampleCount; i++)
                {
                    uint index = branch.SampleOffset + i;
                    BranchSample sample = samples[index];
                    float distanceToTip = hasChild
                        ? branch.RestLength - sample.LocalDistance + remainingChild
                        : branch.RestLength - sample.LocalDistance;
                    samples[index] = sample.WithDistanceToTip(distanceToTip);
                }
            }
        }

        private static BranchGenerationSettings Validate(
            BranchGenerationSettings settings)
        {
            settings.maximumDepth = Mathf.Clamp(settings.maximumDepth, 0, 6);
            settings.branchesPerNode = Mathf.Clamp(settings.branchesPerNode, 1, 4);
            settings.initialLength = Mathf.Max(settings.initialLength, 0.01f);
            settings.lengthScale = Mathf.Clamp(settings.lengthScale, 0.1f, 1f);
            settings.initialRadius = Mathf.Max(settings.initialRadius, 0.001f);
            settings.radiusScale = Mathf.Clamp(settings.radiusScale, 0.1f, 1f);
            settings.branchAngle = Mathf.Clamp(settings.branchAngle, 0f, 85f);
            settings.azimuthSpread = Mathf.Clamp(settings.azimuthSpread, 0f, 180f);
            settings.lengthVariation = Mathf.Clamp01(settings.lengthVariation);
            settings.radiusVariation = Mathf.Clamp01(settings.radiusVariation);
            settings.upwardBias = Mathf.Max(0f, settings.upwardBias);
            settings.minimumSamples = Mathf.Clamp(settings.minimumSamples, 2, 32);
            settings.samplesPerUnitLength = Mathf.Clamp(
                settings.samplesPerUnitLength, 1, 32);
            settings.maximumSamplesPerBranch = Mathf.Max(
                settings.maximumSamplesPerBranch, settings.minimumSamples);
            settings.meshCellSize = Mathf.Max(settings.meshCellSize, 0.01f);
            settings.meshSmoothingRadius =
                Mathf.Max(settings.meshSmoothingRadius, 0f);
            settings.maximumBranches = Mathf.Clamp(settings.maximumBranches, 1, 4096);
            settings.maximumPathSamples = Mathf.Max(2, settings.maximumPathSamples);
            settings.tipInfluenceStart = Mathf.Clamp01(settings.tipInfluenceStart);
            settings.depthResponse = Mathf.Max(0f, settings.depthResponse);
            settings.radiusExponent = Mathf.Max(0f, settings.radiusExponent);
            settings.baseFlexibility = Mathf.Clamp01(settings.baseFlexibility);
            return settings;
        }

        private static float SmoothStep(float start, float end, float value)
        {
            float t = Mathf.Clamp01(
                (value - start) / Mathf.Max(end - start, 0.0001f));
            return t * t * (3f - 2f * t);
        }

        private static void BuildFrame(
            Vector3 tangent,
            out Vector3 normal,
            out Vector3 binormal)
        {
            tangent = tangent.sqrMagnitude > 1e-8f
                ? tangent.normalized
                : Vector3.up;
            Vector3 helper = Mathf.Abs(Vector3.Dot(tangent, Vector3.forward)) > 0.9f
                ? Vector3.right
                : Vector3.forward;
            normal = Vector3.Cross(tangent, helper).normalized;
            binormal = Vector3.Cross(tangent, normal).normalized;
        }

        private sealed class MutableBranch
        {
            public uint Id;
            public uint ParentId;
            public uint ParentSample;
            public int Depth;
            public float InitialRadius;
            public float RestLength;
            public float StartDistanceFromRoot;
            public float DistanceFromRoot;
            public bool Terminal;
            public uint SampleOffset;
            public int SampleCount;
            public Vector3[] Positions;
            public Vector3[] Tangents;
            public Vector3[] Normals;
            public Vector3[] Binormals;
            public float[] LocalDistances;
            public List<uint> ChildIds;
        }

        private readonly struct BranchPathHeaderBuilder
        {
            private readonly BranchRecord _record;

            public BranchPathHeaderBuilder(BranchRecord record)
            {
                _record = record;
            }

            public BranchPathHeader Build() => new BranchPathHeader(
                _record.SampleOffset,
                _record.SampleCount,
                _record.ParentId,
                _record.ParentSample,
                _record.GenerationDepth,
                _record.Terminal,
                _record.RestLength,
                _record.InitialRadius);
        }
    }

    internal static class BranchSampleExtensions
    {
        internal static BranchSample WithDistanceToTip(
            this BranchSample sample,
            float distanceToTip) =>
            new(
                sample.Position,
                sample.Tangent,
                sample.Normal,
                sample.Binormal,
                sample.LocalDistance,
                sample.LocalPosition,
                sample.DistanceFromRoot,
                distanceToTip,
                sample.Radius,
                sample.Flexibility,
                sample.Stiffness,
                sample.TipWeight,
                sample.BranchId,
                sample.GenerationDepth);

        internal static BranchSample WithFlexibility(
            this BranchSample sample,
            float flexibility,
            float stiffness) =>
            new(
                sample.Position,
                sample.Tangent,
                sample.Normal,
                sample.Binormal,
                sample.LocalDistance,
                sample.LocalPosition,
                sample.DistanceFromRoot,
                sample.DistanceToTip,
                sample.Radius,
                flexibility,
                stiffness,
                sample.TipWeight,
                sample.BranchId,
                sample.GenerationDepth);
    }
}
