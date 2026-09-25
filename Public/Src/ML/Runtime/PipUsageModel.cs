// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using BuildXL.ML.Runtime;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Tracing;

namespace BuildXL.ML.PipUsage
{
    /// <summary>Evaluated per-pip CPU utilization percent, peak/average memory MB, and duration.</summary>
    internal readonly struct PipUsagePrediction
    {
        private readonly double m_cpuPercent;
        private readonly double m_peakMemoryMb;
        private readonly double m_averageMemoryMb;
        private readonly double m_durationSec;

        public int CpuPercent => ToNonnegativeInt32(m_cpuPercent);

        public int PeakMemoryMb => ToNonnegativeInt32(m_peakMemoryMb);

        public int AverageMemoryMb => ToNonnegativeInt32(m_averageMemoryMb);

        public uint DurationMilliseconds
        {
            get
            {
                if (double.IsNaN(m_durationSec) || m_durationSec <= 0.0)
                {
                    return 0;
                }

                double milliseconds = m_durationSec * 1000.0;
                return milliseconds >= uint.MaxValue ? uint.MaxValue : (uint)Math.Ceiling(milliseconds);
            }
        }

        internal double GetRawValue(PipUsageTarget target)
        {
            switch (target)
            {
                case PipUsageTarget.Cpu:
                    return m_cpuPercent;
                case PipUsageTarget.Memory:
                    return m_peakMemoryMb;
                case PipUsageTarget.AverageMemory:
                    return m_averageMemoryMb;
                case PipUsageTarget.Duration:
                    return m_durationSec;
                default:
                    throw new ArgumentOutOfRangeException(nameof(target));
            }
        }

        public PipUsagePrediction(double cpuPercent, double peakMemoryMb, double averageMemoryMb, double durationSec)
        {
            m_cpuPercent = cpuPercent;
            m_peakMemoryMb = peakMemoryMb;
            m_averageMemoryMb = averageMemoryMb;
            m_durationSec = durationSec;
        }

        private static int ToNonnegativeInt32(double value)
        {
            if (double.IsNaN(value) || value <= 0.0)
            {
                return 0;
            }

            return value >= int.MaxValue ? int.MaxValue : (int)Math.Ceiling(value);
        }
    }

    /// <summary>
    /// Loads and evaluates the independently versioned Pip Usage model for eligible process pips.
    /// Historical data is optional. Loading or evaluation failures fall back to normal resource estimates and never fail the build.
    /// </summary>
    internal sealed class PipUsageModel
    {
        private const string Unknown = "unknown";
        private static readonly int s_targetCount = Enum.GetValues(typeof(PipUsageTarget)).Length;
        private static readonly Regex s_pipKindPattern = new Regex(@"\|\|\s*(?:Syncronization|Synchronization)\s+Pip\s+For\s+\{\(([^|)]+)", RegexOptions.Compiled);
        private static readonly Regex s_configurationPattern = new Regex("configuration:\"([^\"}]+)", RegexOptions.Compiled);
        private static readonly Regex s_platformPattern = new Regex("platform:\"([^\"}]+)", RegexOptions.Compiled);
        private static readonly Regex s_targetFrameworkPattern = new Regex("targetFramework:\"([^\"}]+)", RegexOptions.Compiled);
        private static readonly Regex s_targetRuntimePattern = new Regex("targetRuntime:\"([^\"}]+)", RegexOptions.Compiled);

        private readonly LightGbmModel[] m_models;
        private readonly IReadOnlyList<string> m_featureNames;
        private readonly int m_featureCount;

        // Pre-computed per-feature metadata for O(1) encoding decisions.
        private readonly bool[] m_isCategorical;
        private readonly Dictionary<string, int>[] m_vocabLookups;
        private readonly double[] m_rareFallbacks;

        private PipUsageModel(
            PipUsageModelSpec spec,
            LightGbmModel[] models)
        {
            m_models = models;
            m_featureNames = spec.Features;
            m_featureCount = m_featureNames.Count;

            var categoricalSet = new HashSet<string>(spec.CategoricalFeatures ?? Array.Empty<string>(), StringComparer.Ordinal);
            m_isCategorical = new bool[m_featureCount];
            m_vocabLookups = new Dictionary<string, int>[m_featureCount];
            m_rareFallbacks = new double[m_featureCount];

            for (int i = 0; i < m_featureCount; i++)
            {
                string name = m_featureNames[i];
                m_isCategorical[i] = categoricalSet.Contains(name);
                if (m_isCategorical[i])
                {
                    m_vocabLookups[i] = BuildVocabLookup(spec, name);
                    m_rareFallbacks[i] = GetRareFallback(spec, name);
                }
            }
        }

        private static Dictionary<string, int> BuildVocabLookup(PipUsageModelSpec spec, string feature)
        {
            if (spec.Vocabularies == null || !spec.Vocabularies.TryGetValue(feature, out IReadOnlyList<string> vocabulary) || vocabulary == null)
            {
                return null;
            }

            var lookup = new Dictionary<string, int>(vocabulary.Count, StringComparer.Ordinal);
            for (int i = 0; i < vocabulary.Count; i++)
            {
                lookup.Add(vocabulary[i], i);
            }

            return lookup;
        }

        private static double GetRareFallback(PipUsageModelSpec spec, string feature)
        {
            if (string.IsNullOrEmpty(spec.RareBucket) || spec.Vocabularies == null ||
                !spec.Vocabularies.TryGetValue(feature, out IReadOnlyList<string> vocabulary) || vocabulary == null)
            {
                return -1.0;
            }

            for (int i = 0; i < vocabulary.Count; i++)
            {
                if (string.Equals(vocabulary[i], spec.RareBucket, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1.0;
        }

        /// <summary>Loads the embedded model resources, or returns <c>null</c> with a diagnostic error.</summary>
        public static PipUsageModel TryLoadEmbedded(out string error)
        {
            error = null;
            try
            {
                Assembly assembly = typeof(PipUsageModel).GetTypeInfo().Assembly;
                PipUsageModelSpec spec;
                using (Stream stream = OpenEmbedded(assembly, "pipUsage.model_spec.json"))
                {
                    if (stream == null)
                    {
                        error = "Embedded resource 'pipUsage.model_spec.json' not found in the Scheduler assembly.";
                        return null;
                    }

                    spec = PipUsageModelSpec.Parse(stream);
                }

                string validationError = spec.Validate();
                if (validationError != null)
                {
                    error = validationError;
                    return null;
                }

                var models = new LightGbmModel[s_targetCount];
                foreach (KeyValuePair<PipUsageTarget, string> modelFile in spec.ModelFiles.GetFiles())
                {
                    LightGbmModel model = ParseEmbeddedModel(assembly, "pipUsage." + modelFile.Value, out error);
                    if (model == null)
                    {
                        return null;
                    }

                    models[(int)modelFile.Key] = model;
                }

                error = ValidateFeatures(models, spec.Features);
                if (error != null)
                {
                    return null;
                }

                return new PipUsageModel(spec, models);
            }
#pragma warning disable EPC12 // Loading is best-effort; the caller receives a concise diagnostic and falls back to normal scheduling defaults.
            catch (Exception ex)
            {
                error = ex.ToString();
                return null;
            }
#pragma warning restore EPC12
        }

        public static bool IsCold(ProcessPipHistoricPerfData historicPerfData)
        {
            return IsCold(historicPerfData.ProcessorsInPercents, historicPerfData.ExeDurationInMs);
        }

        internal static bool IsCold(ushort processorsInPercents, uint exeDurationInMs)
        {
            return processorsInPercents == 0 && exeDurationInMs == 0;
        }

        internal static bool ShouldEvaluate(
            PipUsageMLMode mode,
            ProcessPipHistoricPerfData historicPerfData,
            bool historicDataUnavailable)
        {
            return ShouldEvaluate(mode, IsCold(historicPerfData), historicDataUnavailable);
        }

        internal static bool ShouldEvaluate(PipUsageMLMode mode, bool isCold, bool historicDataUnavailable)
        {
            switch (mode)
            {
                case PipUsageMLMode.Cold:
                    return isCold;
                case PipUsageMLMode.ColdAndWarm:
                    return true;
                case PipUsageMLMode.HistoricDataUnavailable:
                    return historicDataUnavailable;
                default:
                    return false;
            }
        }

        internal static bool CanEvaluate(PipType pipType, AbsolutePath executablePath)
        {
            // This model is trained from process execution telemetry. IPC pips share ProcessMutablePipState,
            // but have different resource semantics and would require a separate training and feature contract.
            return pipType == PipType.Process && executablePath.IsValid;
        }

        /// <summary>Evaluates the model for an eligible process pip.</summary>
        public PipUsagePrediction? TryEvaluate(IPipTable pipTable, PipId pipId, PipExecutionContext context, IConfiguration configuration, ProcessPipHistoricPerfData historicPerfData, out string error)
        {
            error = null;
            try
            {
                PipType pipType = pipTable.GetPipType(pipId);
                if (pipType != PipType.Process || !CanEvaluate(pipType, pipTable.GetProcessExecutablePath(pipId)))
                {
                    error = "Pip Usage ML requires a process pip with a valid executable path.";
                    return null;
                }

                return TryEvaluateCore(pipTable, pipId, context, configuration, historicPerfData, out error);
            }
#pragma warning disable ERP022 // Evaluation is optional; malformed process metadata falls back to normal scheduling estimates for this pip.
            catch (Exception ex)
            {
                error = ex.ToString();
                return null;
            }
#pragma warning restore ERP022
        }

        private PipUsagePrediction? TryEvaluateCore(IPipTable pipTable, PipId pipId, PipExecutionContext context, IConfiguration configuration, ProcessPipHistoricPerfData historicPerfData, out string error)
        {
            AbsolutePath executablePath = pipTable.GetProcessExecutablePath(pipId);
            StringId toolDescription = pipTable.GetProcessToolDescription(pipId);
            ModuleId moduleId = pipTable.GetProcessModuleId(pipId);
            QualifierId qualifierId = pipTable.GetProcessQualifierId(pipId);
            string toolName = executablePath.GetName(context.PathTable).ToString(context.StringTable);
            string tool = toolDescription.IsValid
                ? $"{toolName} ({toolDescription.ToString(context.StringTable)})"
                : toolName;
            string moduleName = moduleId.IsValid ? moduleId.Value.ToString(context.StringTable) : Unknown;
            string[] moduleParts = moduleName.Split(new[] { '.', '_' }, StringSplitOptions.RemoveEmptyEntries);
            string qualifier = qualifierId.IsValid
                ? context.QualifierTable.GetCanonicalDisplayString(qualifierId)
                : string.Empty;
            IReadOnlyDictionary<string, string> traceInfo = configuration.Logging.TraceInfo;
            string toolExtension = Path.GetExtension(toolName);
            var categorical = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [PipUsageFeatureNames.Tool] = tool,
                [PipUsageFeatureNames.ToolExtension] = string.IsNullOrEmpty(toolExtension) ? Unknown : toolExtension.ToLowerInvariant(),
                [PipUsageFeatureNames.ModuleFamily] = moduleParts.Length > 0 ? moduleParts[0] : Unknown,
                [PipUsageFeatureNames.ModuleSubgroup] = moduleParts.Length > 1 ? moduleParts[1] : Unknown,
                [PipUsageFeatureNames.PipKind] = MatchOrUnknown(s_pipKindPattern, qualifier),
                [PipUsageFeatureNames.Configuration] = MatchOrUnknown(s_configurationPattern, qualifier),
                [PipUsageFeatureNames.Platform] = MatchOrUnknown(s_platformPattern, qualifier),
                [PipUsageFeatureNames.TargetFramework] = MatchOrUnknown(s_targetFrameworkPattern, qualifier),
                [PipUsageFeatureNames.TargetRuntime] = MatchOrUnknown(s_targetRuntimePattern, qualifier),
                [PipUsageFeatureNames.Codebase] = TraceInfoOrUnknown(traceInfo, CaptureBuildProperties.CodeBaseKey),
                [PipUsageFeatureNames.StageId] = TraceInfoOrUnknown(traceInfo, CaptureBuildProperties.StageIdKey),
                [PipUsageFeatureNames.Queue] = TraceInfoOrUnknown(traceInfo, TraceInfoExtensions.CloudBuildQueue),
                [PipUsageFeatureNames.Tenant] = TraceInfoOrUnknown(traceInfo, "tenant"),
            };
            bool isCold = IsCold(historicPerfData);
            var numeric = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                [PipUsageFeatureNames.Weight] = pipTable.GetProcessWeight(pipId),
                [PipUsageFeatureNames.NumFileDependencies] = pipTable.GetProcessFileDependencyCount(pipId),
                [PipUsageFeatureNames.NumDirectoryDependencies] = pipTable.GetProcessDirectoryDependencyCount(pipId),
                [PipUsageFeatureNames.NumFileOutputs] = pipTable.GetProcessFileOutputCount(pipId),
                [PipUsageFeatureNames.NumDirectoryOutputs] = pipTable.GetProcessDirectoryOutputCount(pipId),
                [PipUsageFeatureNames.ExpectedProcessorUseInPercents] = isCold ? double.NaN : historicPerfData.ProcessorsInPercents,
                [PipUsageFeatureNames.ExpectedPeakWorkingSetMb] = isCold ? double.NaN : historicPerfData.MemoryCounters.PeakWorkingSetMb,
                [PipUsageFeatureNames.ExpectedAverageWorkingSetMb] = isCold ? double.NaN : historicPerfData.MemoryCounters.AverageWorkingSetMb,
                [PipUsageFeatureNames.ExpectedDurationSec] = isCold ? double.NaN : historicPerfData.ExeDurationInMs / 1000.0,
            };

            return TryEvaluate(categorical, numeric, out error);
        }

        internal PipUsagePrediction? TryEvaluate(
            IReadOnlyDictionary<string, string> categorical,
            IReadOnlyDictionary<string, double> numeric)
        {
            return TryEvaluate(categorical, numeric, out _);
        }

        internal PipUsagePrediction? TryEvaluate(
            IReadOnlyDictionary<string, string> categorical,
            IReadOnlyDictionary<string, double> numeric,
            out string error)
        {
            error = null;
            try
            {
                Span<double> vector = stackalloc double[m_featureCount];
                BuildVector(categorical, numeric, vector);
                Span<double> predictions = stackalloc double[m_models.Length];
                for (int i = 0; i < m_models.Length; i++)
                {
                    if (!TryNormalizePrediction(Expm1(m_models[i].PredictRaw(vector)), out predictions[i]))
                    {
                        error = $"Pip Usage ML returned a non-finite prediction for target {(PipUsageTarget)i}.";
                        return null;
                    }
                }

                return new PipUsagePrediction(
                    cpuPercent: predictions[(int)PipUsageTarget.Cpu],
                    peakMemoryMb: predictions[(int)PipUsageTarget.Memory],
                    averageMemoryMb: predictions[(int)PipUsageTarget.AverageMemory],
                    durationSec: predictions[(int)PipUsageTarget.Duration]);
            }
#pragma warning disable ERP022 // Evaluation is optional; malformed input falls back to normal scheduling estimates for this pip.
            catch (Exception ex)
            {
                error = ex.ToString();
                return null;
            }
#pragma warning restore ERP022
        }

        private void BuildVector(
            IReadOnlyDictionary<string, string> categorical,
            IReadOnlyDictionary<string, double> numeric,
            Span<double> vector)
        {
            for (int i = 0; i < m_featureCount; i++)
            {
                string name = m_featureNames[i];
                if (m_isCategorical[i])
                {
                    if (!categorical.TryGetValue(name, out string value))
                    {
                        throw new InvalidOperationException($"Required categorical feature '{name}' is missing.");
                    }

                    vector[i] = EncodeCategorical(i, value);
                }
                else
                {
                    if (!numeric.TryGetValue(name, out double value))
                    {
                        throw new InvalidOperationException($"Required numeric feature '{name}' is missing.");
                    }

                    vector[i] = value;
                }
            }
        }

        internal static bool TryNormalizePrediction(double value, out double prediction)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                prediction = 0.0;
                return false;
            }

            prediction = Math.Max(0.0, value);
            return true;
        }

        private double EncodeCategorical(int featureIndex, string value)
        {
            Dictionary<string, int> lookup = m_vocabLookups[featureIndex];
            if (lookup == null)
            {
                return -1.0;
            }

            if (value != null && lookup.TryGetValue(value, out int code))
            {
                return code;
            }

            return m_rareFallbacks[featureIndex];
        }

        private static string ValidateFeatures(IReadOnlyList<LightGbmModel> models, IReadOnlyList<string> expectedFeatures)
        {
            for (int modelIndex = 0; modelIndex < models.Count; modelIndex++)
            {
                IReadOnlyList<string> features = models[modelIndex].FeatureNames;
                if (features.Count != expectedFeatures.Count)
                {
                    return "Pip Usage models do not use the same feature schema.";
                }

                for (int featureIndex = 0; featureIndex < features.Count; featureIndex++)
                {
                    string feature = features[featureIndex];
                    if (!PipUsageFeatureNames.IsSupported(feature))
                    {
                        return $"Pip Usage model contains unsupported feature '{feature}'.";
                    }

                    if (!string.Equals(feature, expectedFeatures[featureIndex], StringComparison.Ordinal))
                    {
                        return "Pip Usage models do not use the same feature schema.";
                    }
                }
            }

            return null;
        }

        private static string MatchOrUnknown(Regex pattern, string value)
        {
            Match match = pattern.Match(value);
            return match.Success && !string.IsNullOrEmpty(match.Groups[1].Value) ? match.Groups[1].Value : Unknown;
        }

        private static string TraceInfoOrUnknown(IReadOnlyDictionary<string, string> traceInfo, string key)
        {
            return traceInfo != null && traceInfo.TryGetValue(key, out string value) && !string.IsNullOrEmpty(value) ? value : Unknown;
        }

        private static LightGbmModel ParseEmbeddedModel(Assembly assembly, string resourceName, out string error)
        {
            error = null;
            using (Stream stream = OpenEmbedded(assembly, resourceName))
            {
                if (stream == null)
                {
                    error = $"Embedded model resource '{resourceName}' not found in the Scheduler assembly.";
                    return null;
                }

                return LightGbmModel.Parse(stream);
            }
        }

        private static Stream OpenEmbedded(Assembly assembly, string resourceName)
        {
            Stream stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                return stream;
            }

            foreach (string name in assembly.GetManifestResourceNames())
            {
                if (name == resourceName || name.EndsWith("." + resourceName, StringComparison.Ordinal))
                {
                    return assembly.GetManifestResourceStream(name);
                }
            }

            return null;
        }

        private static double Expm1(double value) => Math.Exp(value) - 1.0;
    }
}
