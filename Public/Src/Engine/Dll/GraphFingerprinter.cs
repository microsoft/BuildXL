// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Utilities.Configuration;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Scheduler.Fingerprints;

namespace BuildXL.Engine
{
    /// <summary>
    /// Computes a fingerprint for looking up a reloadable build graph.
    /// </summary>
    /// <remarks>
    /// This is analogous to a content fingerprint for a pip. Like pip caching, we first compute a precise-enough
    /// fingerprint which might point to a descriptor that contains additional assertions / required values.
    /// A graph fingerprint is the hash of the following values:
    /// - A version number (for invalidating fingerprints as graph changes are made).
    /// - The set of values to be built (TODO: This is relevant for the original value filtering, which is at evaluation time; all other filters apply after graph reload).
    /// - The set of *top level* config files and their hashes
    /// - The desired qualifiers (affects evaluation time and thus the produced graph)
    /// - The machine name (graphs are not currently machine independent)
    /// - The hashes of all BuildXL assemblies; some provide build logic or otherwise affect the spec -> graph evaluation.
    /// In short, this fingerprint should account for any readily-available (especially command-line / invocation sourced) data
    /// that affects what graph would be produced, holding actual spec file contents equal.
    /// </remarks>
    public static class GraphFingerprinter
    {
        internal const int GraphFingerprintVersion = 7;

        /// <summary>
        /// Computes a fingerprint for looking up a reloadable build graph. Null indicates failure
        /// </summary>
        internal static GraphFingerprint TryComputeFingerprint(
            LoggingContext loggingContext,
            IStartupConfiguration startUpConfig,
            IConfiguration config,
            PathTable pathTable,
            IEvaluationFilter evaluationFilter,
            FileContentTable fileContentTable,
            string commitId,
            EngineTestHooksData testHooks)
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(startUpConfig != null);
            Contract.Requires(fileContentTable != null);
            Contract.Requires(evaluationFilter != null);

            Optional<CompositeGraphFingerprint> exactFingerprint = GenerateHash(
                loggingContext,
                startUpConfig,
                config,
                pathTable,
                evaluationFilter,
                fileContentTable,
                commitId,
                testHooks);
            Optional<CompositeGraphFingerprint> compatibleFingerprint;
            if (!exactFingerprint.IsValid)
            {
                return null;
            }

            if (evaluationFilter.CanPerformPartialEvaluation)
            {
                compatibleFingerprint = GenerateHash(
                    loggingContext,
                    startUpConfig,
                    config,
                    pathTable,
                    null,
                    fileContentTable,
                    commitId,
                    testHooks);

                if (!compatibleFingerprint.IsValid)
                {
                    return null;
                }
            }
            else
            {
                compatibleFingerprint = exactFingerprint;
            }

            return new GraphFingerprint(
                exactFingerprint.Value.WithEvaluationFilter(evaluationFilter),
                compatibleFingerprint.Value.WithEvaluationFilter(evaluationFilter));
        }

        private static Optional<CompositeGraphFingerprint> GenerateHash(
            LoggingContext loggingContext,
            IStartupConfiguration startUpConfig,
            IConfiguration config,
            PathTable pathTable,
            IEvaluationFilter partialEvaluationData,
            FileContentTable fileContentTable,
            string commitId,
            EngineTestHooksData testHooks)
        {
            ILayoutConfiguration layout = config.Layout;
            ILoggingConfiguration logging = config.Logging;
            var fingerprintTextElements = new List<(string, string)>();

            using (var hasher = new CoreHashingHelper(recordFingerprintString: true))
            {
                CompositeGraphFingerprint fingerprint = CompositeGraphFingerprint.Zero;

                AddInt(hasher, "version", GraphFingerprintVersion);

                using (var qualifierHasher = new CoreHashingHelper(recordFingerprintString: false))
                {
                    foreach (string qualifier in startUpConfig.QualifierIdentifiers.OrderBy(q => q))
                    {
                        AddText(qualifierHasher, "qualifier", qualifier);
                    }

                    fingerprint.QualifierHash = qualifierHasher.GenerateHash();
                    AddFingerprint(hasher, "Qualifiers", fingerprint.QualifierHash);
                }

                if (partialEvaluationData != null && partialEvaluationData.CanPerformPartialEvaluation)
                {
                    using (var evaluationFilterHasher = new CoreHashingHelper(recordFingerprintString: false))
                    {
                        foreach (string value in partialEvaluationData.ValueNamesToResolveAsStrings.OrderBy(v => v))
                        {
                            AddText(evaluationFilterHasher, "valueName", value);
                        }

                        foreach (string value in partialEvaluationData.ValueDefinitionRootsToResolveAsStrings.OrderBy(v => v))
                        {
                            AddText(evaluationFilterHasher, "valuePath", value);
                        }

                        foreach (string value in partialEvaluationData.ModulesToResolveAsStrings.OrderBy(v => v))
                        {
                            AddText(evaluationFilterHasher, "moduleName", value);
                        }

                        fingerprint.FilterHash = evaluationFilterHasher.GenerateHash();
                        AddFingerprint(hasher, "Values", fingerprint.FilterHash);
                    }
                }

                // These paths get embedded in the result of evaluation. So if any change we must re-evaluate
                AddText(hasher, "ObjectDirectoryPath", layout.ObjectDirectory.IsValid ? layout.ObjectDirectory.ToString(pathTable) : "::null::");
                AddText(hasher, "TempDirectoryPath", layout.TempDirectory.IsValid ? layout.TempDirectory.ToString(pathTable) : "::null::");
                AddText(hasher, "SourceDirectoryPath", layout.SourceDirectory.IsValid ? layout.SourceDirectory.ToString(pathTable) : "::null::");

                // All paths in the graph are relative to 'substTarget' (hence, 'substTarget' must be a part of the fingerprint, but 'substSource' need not be).
                AddText(hasher, "substTarget", logging.SubstTarget.IsValid ? logging.SubstTarget.ToString(pathTable) : "::null::");
                AddText(hasher, "IsCompressed", config.Engine.CompressGraphFiles ? "true" : "false");
                AddText(hasher, "IsSkipHashSourceFile", config.Schedule.SkipHashSourceFile ? "true" : "false");

                // Pip static fingerprints are not always computed because computing them slows down the graph construction by 10%-13%. 
                // Thus, the pip graph may and may not contain pip static fingerprints. To avoid unexpected result due to graph cache hit, 
                // we temporarily add the option for computing pip static fingerprints as part of our graph fingerprint until the fingerprints 
                // are always computed; see Task 1291638.
                AddText(hasher, "ComputePipStaticFingerprints", config.Schedule.ComputePipStaticFingerprints.ToString());

                if (config.Schedule.ComputePipStaticFingerprints)
                {
                    // Pip static fingerprints are part of the graph and include the extra fingerprint salts. 
                    // Thus, when pip static fingerprints are computed, any change to the salt will invalidate the graph because
                    // the pip static fingerprints will no longer be valid. Reusing the graph when the salt changes can result in
                    // underbuild.
                    var extraFingerprintSalts = new ExtraFingerprintSalts(
                        config,
                        PipFingerprintingVersion.TwoPhaseV2,
                        config.Cache.CacheSalt ?? string.Empty,
                         new Scheduler.DirectoryMembershipFingerprinterRuleSet(config, pathTable.StringTable).ComputeSearchPathToolsHash());

                    AddFingerprint(hasher, "ExtraFingerprintSalts", extraFingerprintSalts.CalculatedSaltsFingerprint);
                }

                // Config files
                // Caution: Including the content hash of the config file is how changes to the default pip filter
                // invalidate a cached graph. If the config file content hash is removed, the values that get
                // evaluated because of it must be reflected in the values passed in to this method.
                using (var configHasher = new CoreHashingHelper(recordFingerprintString: false))
                {
                    var configFiles = new List<AbsolutePath> { startUpConfig.ConfigFile };
                    if (startUpConfig.AdditionalConfigFiles != null)
                    {
                        configFiles.AddRange(startUpConfig.AdditionalConfigFiles);
                    }

                    try
                    {
                        foreach (var configPath in configFiles
                            .Select(path => path.ToString(pathTable))
                            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase))
                        {
                            AddContentHash(
                                configHasher,
                                configPath.ToUpperInvariant(),
                                fileContentTable.GetAndRecordContentHashAsync(configPath)
                                    .GetAwaiter()
                                    .GetResult()
                                    .VersionedFileIdentityAndContentInfo.FileContentInfo.Hash);
                        }

                        fingerprint.ConfigFileHash = configHasher.GenerateHash();
                        AddFingerprint(hasher, "ConfigFiles", fingerprint.ConfigFileHash);
                    }
                    catch (BuildXLException ex)
                    {
                        return LogAndReturnFailure(ex);
                    }
                }

                if (!string.IsNullOrEmpty(commitId))
                {
                    using (var commitHasher = new CoreHashingHelper(recordFingerprintString: false))
                    {
                        commitHasher.Add("Commit", commitId);
                        fingerprint.BuildEngineHash = commitHasher.GenerateHash();
                    }

                    AddFingerprint(hasher, "BuildEngine", fingerprint.BuildEngineHash);
                }
                else
                {
                    // BuildXL assemblies. This will invalidate the cached graph if build files change
                    // or if the serialization format changes.
                    try
                    {
                        Action<string, ContentHash> handleBuildFileAndHash = (buildFile, buildFileHash) =>
                        {
                            // Directly add to fingerprint elements for logging, but the hash is represented in the build engine hash
                            fingerprintTextElements.Add((buildFile, buildFileHash.ToString()));
                        };

                        var deployment = testHooks?.AppDeployment ?? AppDeployment.ReadDeploymentManifestFromRunningApp();
                        fingerprint.BuildEngineHash = deployment.ComputeContentHashBasedFingerprint(fileContentTable, handleBuildFileAndHash);
                        AddFingerprint(hasher, "BuildEngine", fingerprint.BuildEngineHash);
                    }
                    catch (BuildXLException ex)
                    {
                        Tracing.Logger.Log.FailedToComputeHashFromDeploymentManifest(loggingContext);
                        Tracing.Logger.Log.FailedToComputeHashFromDeploymentManifestReason(loggingContext, ex.Message);
                        return default(Optional<CompositeGraphFingerprint>);
                    }
                }

                AddText(hasher, "HostOS", startUpConfig.CurrentHost.CurrentOS.ToString());
                AddText(hasher, "HostCpuArchitecture", startUpConfig.CurrentHost.CpuArchitecture.ToString());
                AddText(hasher, "HostIsElevated", CurrentProcess.IsElevated.ToString());

                var salt = string.Empty;

                if (testHooks?.GraphFingerprintSalt != null)
                {
                    salt += testHooks.GraphFingerprintSalt.Value.ToString();
                }

                salt += EngineEnvironmentSettings.DebugGraphFingerprintSalt;

                if (!string.IsNullOrEmpty(salt))
                {
                    hasher.Add("GraphFingerprintSalt", salt);
                }

                fingerprint.OverallFingerprint = new ContentFingerprint(hasher.GenerateHash());
                Tracing.Logger.Log.ElementsOfConfigurationFingerprint(
                    loggingContext,
                    fingerprint.OverallFingerprint.ToString(),
                    string.Join(Environment.NewLine, fingerprintTextElements.Select(kvp => "\t" + kvp.Item1 + " : " + kvp.Item2)));

                return new Optional<CompositeGraphFingerprint>(fingerprint);
            }

            // Local functions
            void AddInt(CoreHashingHelper hasher, string key, int value)
            {
                hasher.Add(key, value);
                fingerprintTextElements.Add(
                   (key, value.ToString(CultureInfo.InvariantCulture)));
            }

            void AddText(CoreHashingHelper hasher, string key, string value)
            {
                hasher.Add(key, value);
                fingerprintTextElements.Add((key, value));
            }

            void AddFingerprint(CoreHashingHelper hasher, string key, Fingerprint value)
            {
                hasher.Add(key, value);
                fingerprintTextElements.Add((key, value.ToString()));
            }

            void AddContentHash(CoreHashingHelper hasher, string key, ContentHash value)
            {
                hasher.Add(key, value);
                fingerprintTextElements.Add((key, value.ToString()));
            }
        }

        private static Optional<CompositeGraphFingerprint> LogAndReturnFailure(BuildXLException ex)
        {
            Tracing.Logger.Log.FailedToComputeGraphFingerprint(Events.StaticContext, ex.LogEventMessage);
            return Optional<CompositeGraphFingerprint>.Invalid;
        }
    }
}
