// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BuildXL.ML.PipUsage
{
    internal enum PipUsageTarget
    {
        Cpu,
        Memory,
        AverageMemory,
        Duration,
    }

    internal static class PipUsageFeatureNames
    {
        public const string Tool = "Tool";
        public const string ToolExtension = "ToolExtension";
        public const string ModuleFamily = "ModuleFamily";
        public const string ModuleSubgroup = "ModuleSubgroup";
        public const string PipKind = "PipKind";
        public const string Configuration = "Configuration";
        public const string Platform = "Platform";
        public const string TargetFramework = "TargetFramework";
        public const string TargetRuntime = "TargetRuntime";
        public const string Codebase = "Codebase";
        public const string StageId = "StageId";
        public const string Queue = "Queue";
        public const string Tenant = "Tenant";
        public const string Weight = "Weight";
        public const string NumFileDependencies = "NumFileDependencies";
        public const string NumDirectoryDependencies = "NumDirectoryDependencies";
        public const string NumFileOutputs = "NumFileOutputs";
        public const string NumDirectoryOutputs = "NumDirectoryOutputs";
        public const string ExpectedProcessorUseInPercents = "ExpectedProcessorUseInPercents";
        public const string ExpectedPeakWorkingSetMb = "ExpectedPeakWorkingSetMb";
        public const string ExpectedAverageWorkingSetMb = "ExpectedAverageWorkingSetMb";
        public const string ExpectedDurationSec = "ExpectedDurationSec";

        public static bool IsCategorical(string name)
        {
            return name == Tool ||
                name == ToolExtension ||
                name == ModuleFamily ||
                name == ModuleSubgroup ||
                name == PipKind ||
                name == Configuration ||
                name == Platform ||
                name == TargetFramework ||
                name == TargetRuntime ||
                name == Codebase ||
                name == StageId ||
                name == Queue ||
                name == Tenant;
        }

        public static bool IsSupported(string name)
        {
            return name == Tool ||
                name == ToolExtension ||
                name == ModuleFamily ||
                name == ModuleSubgroup ||
                name == PipKind ||
                name == Configuration ||
                name == Platform ||
                name == TargetFramework ||
                name == TargetRuntime ||
                name == Codebase ||
                name == StageId ||
                name == Queue ||
                name == Tenant ||
                name == Weight ||
                name == NumFileDependencies ||
                name == NumDirectoryDependencies ||
                name == NumFileOutputs ||
                name == NumDirectoryOutputs ||
                name == ExpectedProcessorUseInPercents ||
                name == ExpectedPeakWorkingSetMb ||
                name == ExpectedAverageWorkingSetMb ||
                name == ExpectedDurationSec;
        }
    }

    internal sealed class ModelFiles
    {
        [JsonPropertyName("cpu")]
        public string Cpu { get; set; }

        [JsonPropertyName("memory")]
        public string Memory { get; set; }

        [JsonPropertyName("average_memory")]
        public string AverageMemory { get; set; }

        [JsonPropertyName("duration")]
        public string Duration { get; set; }

        public bool IsValid =>
            !string.IsNullOrEmpty(Cpu) &&
            !string.IsNullOrEmpty(Memory) &&
            !string.IsNullOrEmpty(AverageMemory) &&
            !string.IsNullOrEmpty(Duration);

        public IEnumerable<KeyValuePair<PipUsageTarget, string>> GetFiles()
        {
            yield return new KeyValuePair<PipUsageTarget, string>(PipUsageTarget.Cpu, Cpu);
            yield return new KeyValuePair<PipUsageTarget, string>(PipUsageTarget.Memory, Memory);
            yield return new KeyValuePair<PipUsageTarget, string>(PipUsageTarget.AverageMemory, AverageMemory);
            yield return new KeyValuePair<PipUsageTarget, string>(PipUsageTarget.Duration, Duration);
        }
    }

    /// <summary>
    /// Typed manifest for the <c>pipUsage</c> model payload.
    /// </summary>
    internal sealed class PipUsageModelSpec
    {
        public int SchemaVersion { get; set; }

        public string ModelKind { get; set; }

        public IReadOnlyList<string> CategoricalFeatures { get; set; }

        public IReadOnlyList<string> Features { get; set; }

        public IReadOnlyDictionary<string, IReadOnlyList<string>> Vocabularies { get; set; }

        public string RareBucket { get; set; }

        public ModelFiles ModelFiles { get; set; }

        public string OutputTransform { get; set; }

        public string Validate()
        {
            if (SchemaVersion != 1)
            {
                return $"Unsupported model schema version {SchemaVersion} (expected 1).";
            }

            if (!string.Equals(ModelKind, "pipUsage", StringComparison.Ordinal))
            {
                return $"Unsupported model kind '{ModelKind ?? "<missing>"}' (expected 'pipUsage').";
            }

            if (ModelFiles == null || !ModelFiles.IsValid)
            {
                return "Pip Usage model manifest is missing one or more required model file names.";
            }

            if (Features == null || Features.Count == 0)
            {
                return "Pip Usage model manifest is missing its feature schema.";
            }

            var features = new HashSet<string>(StringComparer.Ordinal);
            foreach (string feature in Features)
            {
                if (string.IsNullOrEmpty(feature) || !PipUsageFeatureNames.IsSupported(feature) || !features.Add(feature))
                {
                    return $"Pip Usage model manifest contains invalid or duplicate feature '{feature ?? "<missing>"}'.";
                }
            }

            if (CategoricalFeatures == null || Vocabularies == null || string.IsNullOrEmpty(RareBucket))
            {
                return "Pip Usage model manifest is missing categorical feature metadata.";
            }

            var categoricalFeatures = new HashSet<string>(StringComparer.Ordinal);
            foreach (string feature in CategoricalFeatures)
            {
                if (!features.Contains(feature) || !PipUsageFeatureNames.IsCategorical(feature) || !categoricalFeatures.Add(feature))
                {
                    return $"Pip Usage model manifest contains invalid or duplicate categorical feature '{feature ?? "<missing>"}'.";
                }

                if (!Vocabularies.TryGetValue(feature, out IReadOnlyList<string> vocabulary) || vocabulary == null || vocabulary.Count == 0)
                {
                    return $"Pip Usage model manifest is missing a vocabulary for categorical feature '{feature}'.";
                }

                bool hasRareBucket = false;
                var vocabularyValues = new HashSet<string>(StringComparer.Ordinal);
                foreach (string value in vocabulary)
                {
                    if (string.IsNullOrEmpty(value) || !vocabularyValues.Add(value))
                    {
                        return $"Pip Usage model vocabulary for '{feature}' contains an empty or duplicate value.";
                    }

                    hasRareBucket |= string.Equals(value, RareBucket, StringComparison.Ordinal);
                }

                if (!hasRareBucket)
                {
                    return $"Pip Usage model vocabulary for '{feature}' is missing rare bucket '{RareBucket}'.";
                }
            }

            foreach (string feature in Vocabularies.Keys)
            {
                if (!categoricalFeatures.Contains(feature))
                {
                    return $"Pip Usage model manifest contains a vocabulary for undeclared categorical feature '{feature}'.";
                }
            }

            foreach (string feature in Features)
            {
                if (PipUsageFeatureNames.IsCategorical(feature) != categoricalFeatures.Contains(feature))
                {
                    return $"Pip Usage model manifest misclassifies feature '{feature}'.";
                }
            }

            if (!string.Equals(OutputTransform, "expm1", StringComparison.Ordinal))
            {
                return "Pip Usage model manifest specifies an unsupported output transform (only 'expm1' is supported).";
            }

            return null;
        }

        public static PipUsageModelSpec Parse(Stream jsonStream)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            PipUsageModelSpec spec = JsonSerializer.Deserialize<PipUsageModelSpec>(jsonStream, options);
            if (spec == null)
            {
                throw new FormatException("model_spec.json deserialized to null.");
            }

            return spec;
        }
    }
}
