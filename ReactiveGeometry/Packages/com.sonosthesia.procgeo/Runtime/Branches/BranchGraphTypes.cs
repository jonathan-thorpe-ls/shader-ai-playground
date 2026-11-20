using System;
using UnityEngine;

namespace Sonosthesia.ProcGeo
{
    [Serializable]
    public struct BranchGenerationSettings
    {
        [Min(0)] public int maximumDepth;
        [Min(1)] public int branchesPerNode;
        [Min(0.01f)] public float initialLength;
        [Range(0.1f, 1f)] public float lengthScale;
        [Min(0.001f)] public float initialRadius;
        [Range(0.1f, 1f)] public float radiusScale;
        [Range(0f, 85f)] public float branchAngle;
        [Range(0f, 180f)] public float azimuthSpread;
        [Range(0f, 1f)] public float lengthVariation;
        [Range(0f, 1f)] public float radiusVariation;
        [Min(0f)] public float upwardBias;
        [Min(1)] public int minimumSamples;
        [Min(1)] public int samplesPerUnitLength;
        [Min(1)] public int maximumSamplesPerBranch;
        [Min(3)] public int radialSegments;
        [Min(1)] public int maximumBranches;
        [Min(2)] public int maximumPathSamples;
        [Range(0f, 1f)] public float tipInfluenceStart;
        [Min(0f)] public float depthResponse;
        [Min(0f)] public float radiusExponent;
        [Range(0f, 1f)] public float baseFlexibility;
        public int seed;

        public static BranchGenerationSettings Default => new()
        {
            maximumDepth = 3,
            branchesPerNode = 2,
            initialLength = 2.2f,
            lengthScale = 0.72f,
            initialRadius = 0.22f,
            radiusScale = 0.66f,
            branchAngle = 28f,
            azimuthSpread = 120f,
            lengthVariation = 0.12f,
            radiusVariation = 0.06f,
            upwardBias = 0.15f,
            minimumSamples = 3,
            samplesPerUnitLength = 5,
            maximumSamplesPerBranch = 24,
            radialSegments = 8,
            maximumBranches = 128,
            maximumPathSamples = 2048,
            tipInfluenceStart = 0.25f,
            depthResponse = 0.55f,
            radiusExponent = 0.75f,
            baseFlexibility = 0.95f,
            seed = 104729
        };
    }

    public readonly struct BranchSample
    {
        public readonly Vector3 Position;
        public readonly Vector3 Tangent;
        public readonly Vector3 Normal;
        public readonly Vector3 Binormal;
        public readonly float LocalDistance;
        public readonly float LocalPosition;
        public readonly float DistanceFromRoot;
        public readonly float DistanceToTip;
        public readonly float Radius;
        public readonly float Flexibility;
        public readonly float Stiffness;
        public readonly float TipWeight;
        public readonly uint BranchId;
        public readonly uint GenerationDepth;

        public BranchSample(
            Vector3 position,
            Vector3 tangent,
            Vector3 normal,
            Vector3 binormal,
            float localDistance,
            float localPosition,
            float distanceFromRoot,
            float distanceToTip,
            float radius,
            float flexibility,
            float stiffness,
            float tipWeight,
            uint branchId,
            uint generationDepth)
        {
            Position = position;
            Tangent = tangent;
            Normal = normal;
            Binormal = binormal;
            LocalDistance = localDistance;
            LocalPosition = localPosition;
            DistanceFromRoot = distanceFromRoot;
            DistanceToTip = distanceToTip;
            Radius = radius;
            Flexibility = flexibility;
            Stiffness = stiffness;
            TipWeight = tipWeight;
            BranchId = branchId;
            GenerationDepth = generationDepth;
        }
    }

    public readonly struct BranchRecord
    {
        public readonly uint Id;
        public readonly uint ParentId;
        public readonly uint ParentSample;
        public readonly uint SampleOffset;
        public readonly uint SampleCount;
        public readonly uint GenerationDepth;
        public readonly float RestLength;
        public readonly float InitialRadius;
        public readonly float StartDistanceFromRoot;
        public readonly bool Terminal;

        public BranchRecord(
            uint id,
            uint parentId,
            uint parentSample,
            uint sampleOffset,
            uint sampleCount,
            uint generationDepth,
            float restLength,
            float initialRadius,
            float startDistanceFromRoot,
            bool terminal)
        {
            Id = id;
            ParentId = parentId;
            ParentSample = parentSample;
            SampleOffset = sampleOffset;
            SampleCount = sampleCount;
            GenerationDepth = generationDepth;
            RestLength = restLength;
            InitialRadius = initialRadius;
            StartDistanceFromRoot = startDistanceFromRoot;
            Terminal = terminal;
        }
    }

    public readonly struct BranchPathHeader
    {
        public readonly uint SampleOffset;
        public readonly uint SampleCount;
        public readonly uint ParentBranch;
        public readonly uint ParentSample;
        public readonly uint GenerationDepth;
        public readonly uint Terminal;
        public readonly float RestLength;
        public readonly float InitialRadius;

        public BranchPathHeader(
            uint sampleOffset,
            uint sampleCount,
            uint parentBranch,
            uint parentSample,
            uint generationDepth,
            bool terminal,
            float restLength,
            float initialRadius)
        {
            SampleOffset = sampleOffset;
            SampleCount = sampleCount;
            ParentBranch = parentBranch;
            ParentSample = parentSample;
            GenerationDepth = generationDepth;
            Terminal = terminal ? 1u : 0u;
            RestLength = restLength;
            InitialRadius = initialRadius;
        }
    }

    public sealed class BranchGraphPlan
    {
        internal BranchGraphPlan(
            BranchRecord[] branches,
            BranchSample[] samples,
            BranchPathHeader[] pathHeaders,
            int maximumGenerationDepth,
            Bounds bounds)
        {
            Branches = branches ?? throw new ArgumentNullException(nameof(branches));
            Samples = samples ?? throw new ArgumentNullException(nameof(samples));
            PathHeaders = pathHeaders ?? throw new ArgumentNullException(nameof(pathHeaders));
            MaximumGenerationDepth = maximumGenerationDepth;
            Bounds = bounds;
        }

        public BranchRecord[] Branches { get; }
        public BranchSample[] Samples { get; }
        public BranchPathHeader[] PathHeaders { get; }
        public int MaximumGenerationDepth { get; }
        public Bounds Bounds { get; }
        public int BranchCount => Branches.Length;
        public int SampleCount => Samples.Length;

        public uint ParentAttachmentSample(int branchIndex)
        {
            if ((uint)branchIndex >= (uint)Branches.Length)
                throw new ArgumentOutOfRangeException(nameof(branchIndex));
            return Branches[branchIndex].ParentSample;
        }
    }

    public interface IBranchGraphGenerator
    {
        BranchGraphPlan Plan(BranchGenerationSettings settings);
    }
}
