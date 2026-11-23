using System.IO;
using Sonosthesia.Signals;
using UnityEngine;

namespace Sonosthesia.Signals.Xas
{
    [CreateAssetMenu(
        fileName = "Xas Analysis",
        menuName = "Sonosthesia/XAS Analysis Asset")]
    public sealed class XasAnalysisAsset : ScriptableObject
    {
        [SerializeField] private string streamingAssetsRelativePath;
        [SerializeField] private string displayName;
        [SerializeField] private double durationSeconds;

        public string DisplayName => string.IsNullOrWhiteSpace(displayName)
            ? name
            : displayName;
        public string StreamingAssetsRelativePath => streamingAssetsRelativePath;
        public double DurationSeconds => durationSeconds;
        public ControlSignalProfile Profile => XasControlSignalProfile.Instance;

        public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(streamingAssetsRelativePath)
                   && Path.GetExtension(streamingAssetsRelativePath)
                       .Equals(".xas", System.StringComparison.OrdinalIgnoreCase)
                   && !Path.IsPathRooted(streamingAssetsRelativePath)
                   && !streamingAssetsRelativePath.Contains("..");
        }

        public string ResolvePath()
        {
            if (!IsValid())
                throw new InvalidDataException($"Invalid XAS asset path on '{name}'.");
            return Path.Combine(Application.streamingAssetsPath, streamingAssetsRelativePath);
        }

        public void Configure(
            string relativePath,
            string nameOverride,
            double duration)
        {
            streamingAssetsRelativePath = relativePath;
            displayName = nameOverride;
            durationSeconds = duration;
        }
    }
}
