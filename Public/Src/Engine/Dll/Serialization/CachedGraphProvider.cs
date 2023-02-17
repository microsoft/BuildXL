// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Engine.Cache.Fingerprints.SinglePhase;
using BuildXL.Engine.Cache.Fingerprints.TwoPhase;
using BuildXL.Scheduler;
using BuildXL.Storage;
using BuildXL.Storage.ChangeTracking;
using BuildXL.Storage.Fingerprints;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using static BuildXL.Utilities.Core.FormattableStringEx;

namespace BuildXL.Engine
{
    /// <summary>
    /// Class responsible for providing cached graph.
    /// </summary>
    /// <remarks>
    /// See details documentation in BuildXL's OneNote section.
    /// </remarks>
    internal sealed class CachedGraphProvider
    {
        private const int InputDifferencesLimit = 25;
        private const int FailedHashLimit = 25;
        private const int MaxHopCount = 10;

        private readonly EngineCache m_cache;
        private readonly EngineContext m_engineContext;
        private readonly FileContentTable m_fileContentTable;
        private readonly LoggingContext m_loggingContext;
        private readonly int m_maxDegreeOfParallelism;

        private PathTable PathTable => m_engineContext.PathTable;

        private const string NullPathMarker = "::Null::";

        /// <summary>
        /// Creates an instance of <see cref="CachedGraphProvider" />.
        /// </summary>
        public CachedGraphProvider(
            LoggingContext loggingContext,
            EngineContext engineContext,
            EngineCache cache,
            FileContentTable fileContentTable,
            int maxDegreeOfParallelism)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(engineContext != null);
            Contract.Requires(cache != null);
            Contract.Requires(fileContentTable != null);
            Contract.Requires(maxDegreeOfParallelism > 0);

            m_loggingContext = loggingContext;
            m_engineContext = engineContext;
            m_cache = cache;
            m_fileContentTable = fileContentTable;
            m_maxDegreeOfParallelism = maxDegreeOfParallelism;
        }

        /// <summary>
        /// Gets cached <see cref="PipGraphCacheDescriptor"/>.
        /// </summary>
        /// <returns>Returns fetched <see cref="PipGraphCacheDescriptor"/> if hit, otherwise null.</returns>
        public async Task<PipGraphCacheDescriptor> TryGetPipGraphCacheDescriptorAsync(
            GraphFingerprint graphFingerprint,
            BuildParameters.IBuildParameters buildParameters,
            IReadOnlyDictionary<string, IMount> mounts)
        {
            Contract.Requires(graphFingerprint != null);
            Contract.Requires(buildParameters != null);
            Contract.Requires(mounts != null);

            GetPipGraphCacheDescriptorResult compatibleResult;

            if ((compatibleResult = await
                TryGetPipGraphCacheDescriptorAsync(
                    graphFingerprint.CompatibleFingerprint.OverallFingerprint,
                    buildParameters,
                    mounts,
                    ProviderContext.CompatibleGet)).IsHit)
            {
                LogGetPipGraphDescriptorFromCache(compatibleResult, GetPipGraphCacheDescriptorResult.CreateForNone());
                return compatibleResult.PipGraphCacheDescriptor;
            }

            GetPipGraphCacheDescriptorResult exactResult = await TryGetPipGraphCacheDescriptorAsync(
                graphFingerprint.ExactFingerprint.OverallFingerprint,
                buildParameters,
                mounts,
                ProviderContext.ExactGet);

            LogGetPipGraphDescriptorFromCache(compatibleResult, exactResult);

            return exactResult.IsHit ? exactResult.PipGraphCacheDescriptor : null;
        }

        private async Task<GetPipGraphCacheDescriptorResult> TryGetPipGraphCacheDescriptorAsync(
            ContentFingerprint graphFingerprint,
            BuildParameters.IBuildParameters buildParameters,
            IReadOnlyDictionary<string, IMount> mounts,
            ProviderContext providerContext)
        {
            Contract.Requires(buildParameters != null);
            Contract.Requires(mounts != null);
            Contract.Requires(
                providerContext == ProviderContext.CompatibleGet 
                || providerContext == ProviderContext.ExactGet);

            var singlePhaseFingerprintStore = new SinglePhaseFingerprintStoreAdapter(
                m_loggingContext,
                m_engineContext,
                m_cache.TwoPhaseFingerprintStore,
                m_cache.ArtifactContentCache);

            int hopCount = 0;
            var fingerprintChains = new List<ContentFingerprint>(MaxHopCount);
            var currentFingerprint = graphFingerprint;
            var sw = Stopwatch.StartNew();
            var getFingerprintEntrySw = new StopwatchVar();
            var hashPipGraphInputSw = new StopwatchVar();
            MismatchedInputCollection lastRecentlyMismatchedInputs = null;

            while (true)
            {
                if (hopCount > MaxHopCount)
                {
                    return GetPipGraphCacheDescriptorResult.CreateForFailed(
                        GetPipGraphCacheDescriptorResultKind.ExceededMaxHopCount,
                        fingerprintChains,
                        hopCount,
                        string.Empty,
                        sw.ElapsedMilliseconds,
                        (long) hashPipGraphInputSw.TotalElapsed.TotalMilliseconds,
                        (long) getFingerprintEntrySw.TotalElapsed.TotalMilliseconds);
                }

                ++hopCount;
                fingerprintChains.Add(currentFingerprint);
                Possible<PipFingerprintEntry> maybeMetadata;
                CacheQueryData cacheQueryData = new CacheQueryData();

                using (getFingerprintEntrySw.Start())
                {
                    maybeMetadata = await singlePhaseFingerprintStore.TryGetFingerprintEntryAsync(currentFingerprint, cacheQueryData);
                }

                if (!maybeMetadata.Succeeded)
                {
                    // Failed.
                    return GetPipGraphCacheDescriptorResult.CreateForFailed(
                        GetPipGraphCacheDescriptorResultKind.FailedGetFingerprintEntry,
                        fingerprintChains,
                        hopCount,
                        maybeMetadata.Failure.DescribeIncludingInnerFailures(),
                        sw.ElapsedMilliseconds,
                        (long)hashPipGraphInputSw.TotalElapsed.TotalMilliseconds,
                        (long)getFingerprintEntrySw.TotalElapsed.TotalMilliseconds);
                }

                PipFingerprintEntry metadata = maybeMetadata.Result;

                if (metadata == null)
                {
                    // Miss.
                    return GetPipGraphCacheDescriptorResult.CreateForMiss(
                        fingerprintChains,
                        lastRecentlyMismatchedInputs,
                        hopCount,
                        sw.ElapsedMilliseconds,
                        (long) hashPipGraphInputSw.TotalElapsed.TotalMilliseconds,
                        (long) getFingerprintEntrySw.TotalElapsed.TotalMilliseconds);
                }

                switch (metadata.Kind)
                {
                    case PipFingerprintEntryKind.GraphDescriptor:
                        // Hit.
                        return GetPipGraphCacheDescriptorResult.CreateForHit(
                            (PipGraphCacheDescriptor) metadata.Deserialize(m_engineContext.CancellationToken, cacheQueryData),
                            fingerprintChains,
                            hopCount,
                            sw.ElapsedMilliseconds,
                            (long) hashPipGraphInputSw.TotalElapsed.TotalMilliseconds,
                            (long) getFingerprintEntrySw.TotalElapsed.TotalMilliseconds);

                    case PipFingerprintEntryKind.GraphInputDescriptor:
                        var pipGraphInputs = (PipGraphInputDescriptor) metadata.Deserialize(m_engineContext.CancellationToken, cacheQueryData);
                        Possible<ContentFingerprint> possibleFingerprint;

                        using (hashPipGraphInputSw.Start())
                        {
                            possibleFingerprint = TryHashPipGraphInputDescriptor(
                                currentFingerprint,
                                buildParameters,
                                mounts,
                                pipGraphInputs,
                                providerContext,
                                hopCount,
                                null,
                                out lastRecentlyMismatchedInputs);
                        }

                        if (!possibleFingerprint.Succeeded)
                        {
                            // Failed.
                            return
                                GetPipGraphCacheDescriptorResult.CreateForFailed(
                                    GetPipGraphCacheDescriptorResultKind.FailedHashPipGraphInputDescriptor,
                                    fingerprintChains,
                                    hopCount,
                                    possibleFingerprint.Failure.DescribeIncludingInnerFailures(),
                                    sw.ElapsedMilliseconds,
                                    (long) hashPipGraphInputSw.TotalElapsed.TotalMilliseconds,
                                    (long) getFingerprintEntrySw.TotalElapsed.TotalMilliseconds);
                        }

                        currentFingerprint = possibleFingerprint.Result;
                        break;

                    default:
                        // Failed.
                        return GetPipGraphCacheDescriptorResult.CreateForFailed(
                            GetPipGraphCacheDescriptorResultKind.UnexpectedFingerprintEntryKind,
                            fingerprintChains,
                            hopCount,
                            metadata.Kind.ToString(),
                            sw.ElapsedMilliseconds,
                            (long) hashPipGraphInputSw.TotalElapsed.TotalMilliseconds,
                            (long) getFingerprintEntrySw.TotalElapsed.TotalMilliseconds);
                }
            }
        }

        private void LogGetPipGraphDescriptorFromCache(GetPipGraphCacheDescriptorResult compatible, GetPipGraphCacheDescriptorResult exact)
        {
            Tracing.Logger.Log.GetPipGraphDescriptorFromCache(
                m_loggingContext,
                compatible.Kind.ToString(),
                compatible.HopCount,
                compatible.Reason,
                unchecked((int) compatible.ElapsedTimeMs),
                unchecked((int) compatible.HashGraphInputsElapsedTimeMs),
                unchecked((int) compatible.GetFingerprintEntryElapsedTimeMs),
                fingerprintChainToString(compatible),
                getMissReason(compatible),
                exact.Kind.ToString(),
                exact.HopCount,
                exact.Reason,
                unchecked((int) exact.ElapsedTimeMs),
                unchecked((int) exact.HashGraphInputsElapsedTimeMs),
                unchecked((int) exact.GetFingerprintEntryElapsedTimeMs),
                fingerprintChainToString(exact),
                getMissReason(exact));

            static string fingerprintChainToString(GetPipGraphCacheDescriptorResult result) => 
                result.Fingerprints != null && result.Fingerprints.Count > 0
                ? string.Join(", ", result.Fingerprints.Select(f => f.ToString()))
                : string.Empty;

            static string getMissReason(GetPipGraphCacheDescriptorResult result)
            {
                const string Prefix = "\t\t";

                return result.Kind != GetPipGraphCacheDescriptorResultKind.Miss
                    ? string.Empty
                    : ((result.LastRecentlyMismatchedInputs == null || !result.LastRecentlyMismatchedInputs.HasMismatch)
                        ? Environment.NewLine + Prefix + "Mismatched pip graph fingerprint"
                        : Environment.NewLine + result.LastRecentlyMismatchedInputs.ToString(Prefix));
            }

        }

        /// <summary>
        /// Stores <see cref="PipGraphCacheDescriptor"/> to the cache.
        /// </summary>
        /// <returns>
        /// The input <see cref="PipGraphCacheDescriptor"/> if store operation is successful,
        /// or different <see cref="PipGraphCacheDescriptor"/> when a conflict occurs,
        /// otherwise null.
        /// </returns>
        public async Task<PipGraphCacheDescriptor> TryStorePipGraphCacheDescriptorAsync(
            InputTracker inputTracker,
            BuildParameters.IBuildParameters buildParametersImpactingBuild,
            IReadOnlyDictionary<string, IMount> mountsImpactingBuild,
            BuildParameters.IBuildParameters availableBuildParameters,
            IReadOnlyDictionary<string, IMount> availableMounts,
            PipGraphCacheDescriptor pipGraphCacheDescriptor)
        {
            Contract.Requires(inputTracker != null);
            Contract.Requires(inputTracker.IsEnabled);
            Contract.Requires(buildParametersImpactingBuild != null);
            Contract.Requires(mountsImpactingBuild != null);
            Contract.Requires(availableBuildParameters != null);
            Contract.Requires(availableMounts != null);
            Contract.Requires(pipGraphCacheDescriptor != null);

            var graphFingerprint = inputTracker.GraphFingerprint;
            var observedGraphInputs = CreateObservedGraphInputs(inputTracker, buildParametersImpactingBuild, mountsImpactingBuild);

            // Instead of using build parameters and mounts that impact the build, we use the available ones for computing the hashes of graph inputs
            // while traversing the chain of fingerprint look ups for storing the pip graph cache descriptor. We use the available ones because
            // they are the ones used when we fetch the pip graph cache descriptor in <code>TryGetPipGraphCacheDescriptorAsync</code>.
            //
            // Suppose that one of the graph input is an environment variable E, i.e., one of the specs, S, queries (reads/probes) E. 
            // When we store the pip graph cache descriptor, I = { ... E ... } will be a graph input in the chain of look-ups. 
            //
            // Now, if we modify S such that it no longer queries E, we expect to get a graph cache miss because S's hash is different.
            // But note that during the fetch when we compute the hash of I, we use the available value of E because at the fetch time
            // we don't know environment variables that would impact the build. Suppose that, when we store the pip graph cache descriptor,
            // we are using the environment variables that impact the build (yes, we have it because graph cache miss results in a spec evaluation), i.e.,
            // we are hashing the pair (E, NULL) when we compute the hash of I. NULL here means that E is not used during the spec evaluation, and thus not impact
            // the build.
            //
            // Now, if we run the second build, we will have a graph cache miss again because when we hash I during fetch, we are using the available environment variable values.
            //
            // The above example also shows a pathological case where we may never get a graph cache hit. Suppose that E's value is a timestamp (or directory whose name contains timestamp).
            // E changes from one build to another. If we mistakenly include E in the first build, then it will stay in I forever. If now we stop referring to E
            // during evaluation, E will still be used during storing and fetching the pip graph cache descriptor because I is in the chain of look-ups.
            // The only way out is to modify the graph fingerprint, and the easiest way is to pass a graph fingerprint salt.
            var storeResult = await TryStorePipGraphCacheDescriptorAsync(
                inputTracker,
                availableBuildParameters,
                availableMounts,
                graphFingerprint.OverallFingerprint,
                observedGraphInputs,
                pipGraphCacheDescriptor);

            LogStorePipGraphCacheDescriptorToCache(storeResult);

            return storeResult.PipGraphCacheDescriptor;
        }

        private async Task<StorePipGraphCacheDescriptorResult> TryStorePipGraphCacheDescriptorAsync(
            InputTracker inputTracker,
            BuildParameters.IBuildParameters buildParametersForHashingGraphInput,
            IReadOnlyDictionary<string, IMount> mountsForHashingGraphInput,
            ContentFingerprint graphFingerprint,
            ObservedGraphInputs observedGraphInputs,
            PipGraphCacheDescriptor pipGraphCacheDescriptor)
        {
            Contract.Requires(inputTracker != null);
            Contract.Requires(buildParametersForHashingGraphInput != null);
            Contract.Requires(mountsForHashingGraphInput != null);
            Contract.Requires(pipGraphCacheDescriptor != null);

            var singlePhaseFingerprintStore = new SinglePhaseFingerprintStoreAdapter(
                m_loggingContext,
                m_engineContext,
                m_cache.TwoPhaseFingerprintStore,
                m_cache.ArtifactContentCache);

            var currentFingerprint = graphFingerprint;
            PipFingerprintEntry graphEntry = pipGraphCacheDescriptor.ToEntry();
            PipGraphInputDescriptor observedGraphInputsToStore = observedGraphInputs.IsEmpty
                ? null
                : observedGraphInputs.ToPipGraphInputDescriptor(PathTable);

            var fingerprintChains = new List<ContentFingerprint>(MaxHopCount);
            int hopCount = 0;
            var sw = Stopwatch.StartNew();
            var storeFingerprintEntrySw = new StopwatchVar();
            var hashPipGraphInputSw = new StopwatchVar();
            var loadAndDeserializeSw = new StopwatchVar();

            while (true)
            {
                if (hopCount > MaxHopCount)
                {
                    return StorePipGraphCacheDescriptorResult.CreateForFailed(
                        StorePipGraphCacheDescriptorResultKind.ExceededMaxHopCount,
                        fingerprintChains,
                        hopCount,
                        string.Empty,
                        sw.ElapsedMilliseconds,
                        (long) hashPipGraphInputSw.TotalElapsed.TotalMilliseconds,
                        (long) storeFingerprintEntrySw.TotalElapsed.TotalMilliseconds,
                        (long) loadAndDeserializeSw.TotalElapsed.TotalMilliseconds);
                }

                ++hopCount;
                fingerprintChains.Add(currentFingerprint);
                var cacheQueryData = new CacheQueryData();
                Possible<CacheEntryPublishResult, Failure> storeResult;

                using (storeFingerprintEntrySw.Start())
                {
                    PipFingerprintEntry fingerprintEntryToStore = observedGraphInputsToStore?.ToEntry() ?? graphEntry;
                    storeResult = await singlePhaseFingerprintStore.TryStoreFingerprintEntryAsync(
                        currentFingerprint,
                        fingerprintEntryToStore,
                        previousEntry: null,
                        replaceExisting: fingerprintEntryToStore == graphEntry, // Replace existing if we are about to store the graph.
                        cacheQueryData: cacheQueryData);
                }

                if (!storeResult.Succeeded)
                {
                    return StorePipGraphCacheDescriptorResult.CreateForFailed(
                        StorePipGraphCacheDescriptorResultKind.FailedStoreFingerprintEntry,
                        fingerprintChains,
                        hopCount,
                        storeResult.Failure.DescribeIncludingInnerFailures(),
                        sw.ElapsedMilliseconds,
                        (long) hashPipGraphInputSw.TotalElapsed.TotalMilliseconds,
                        (long) storeFingerprintEntrySw.TotalElapsed.TotalMilliseconds,
                        (long) loadAndDeserializeSw.TotalElapsed.TotalMilliseconds);
                }

                if (storeResult.Result.Status == CacheEntryPublishStatus.RejectedDueToConflictingEntry)
                {
                    var conflictingEntry = storeResult.Result.ConflictingEntry;
                    cacheQueryData.MetadataHash = conflictingEntry.MetadataHash;

                    Possible<PipFingerprintEntry, Failure> descriptor;

                    using (loadAndDeserializeSw.Start())
                    {
                        descriptor = await singlePhaseFingerprintStore.TryLoadAndDeserializeContent(conflictingEntry.MetadataHash);
                    }

                    if (!descriptor.Succeeded || descriptor.Result == null)
                    {
                        return StorePipGraphCacheDescriptorResult.CreateForFailed(
                            StorePipGraphCacheDescriptorResultKind.FailedLoadAndDeserializeContent,
                            fingerprintChains,
                            hopCount,
                            !descriptor.Succeeded 
                                ? descriptor.Failure.DescribeIncludingInnerFailures()
                                : I($"Conflict cache entry with hash '{conflictingEntry.MetadataHash}' is not available"),
                            sw.ElapsedMilliseconds,
                            (long) hashPipGraphInputSw.TotalElapsed.TotalMilliseconds,
                            (long) storeFingerprintEntrySw.TotalElapsed.TotalMilliseconds,
                            (long) loadAndDeserializeSw.TotalElapsed.TotalMilliseconds);
                    }

                    if (observedGraphInputs.IsEmpty)
                    {
                        // conflictingValue != null && obsInput.Empty
                        //     ==>
                        //         if (conflictingValue is GraphDescriptor) return conflictingValue
                        //         else currentFingerprint = ##(currentFingerprint + conflictingValue)
                        switch (descriptor.Result.Kind)
                        {
                            case PipFingerprintEntryKind.GraphDescriptor:
                                return
                                    StorePipGraphCacheDescriptorResult.CreateForConflict(
                                        (PipGraphCacheDescriptor) descriptor.Result.Deserialize(m_engineContext.CancellationToken, cacheQueryData),
                                        fingerprintChains,
                                        hopCount,
                                        sw.ElapsedMilliseconds,
                                        (long) hashPipGraphInputSw.TotalElapsed.TotalMilliseconds,
                                        (long) storeFingerprintEntrySw.TotalElapsed.TotalMilliseconds,
                                        (long) loadAndDeserializeSw.TotalElapsed.TotalMilliseconds);
                            case PipFingerprintEntryKind.GraphInputDescriptor:
                                Possible<ContentFingerprint> possibleFingerprint;
                                using (hashPipGraphInputSw.Start())
                                {
                                    possibleFingerprint = TryHashPipGraphInputDescriptor(
                                        currentFingerprint,
                                        buildParametersForHashingGraphInput,
                                        mountsForHashingGraphInput,
                                        (PipGraphInputDescriptor) descriptor.Result.Deserialize(m_engineContext.CancellationToken, cacheQueryData),
                                        ProviderContext.Store,
                                        hopCount,
                                        inputTracker,
                                        out var _);
                                }

                                if (!possibleFingerprint.Succeeded)
                                {
                                    return
                                        StorePipGraphCacheDescriptorResult.CreateForFailed(
                                            StorePipGraphCacheDescriptorResultKind.FailedHashPipGraphInputDescriptor,
                                            fingerprintChains,
                                            hopCount,
                                            possibleFingerprint.Failure.DescribeIncludingInnerFailures(),
                                            sw.ElapsedMilliseconds,
                                            (long) hashPipGraphInputSw.TotalElapsed.TotalMilliseconds,
                                            (long) storeFingerprintEntrySw.TotalElapsed.TotalMilliseconds,
                                            (long) loadAndDeserializeSw.TotalElapsed.TotalMilliseconds);
                                }

                                currentFingerprint = possibleFingerprint.Result;
                                break;

                            default:
                                return
                                    StorePipGraphCacheDescriptorResult.CreateForFailed(
                                        StorePipGraphCacheDescriptorResultKind.UnexpectedFingerprintEntryKind,
                                        fingerprintChains,
                                        hopCount,
                                        descriptor.Result.Kind.ToString(),
                                        sw.ElapsedMilliseconds,
                                        (long) hashPipGraphInputSw.TotalElapsed.TotalMilliseconds,
                                        (long) storeFingerprintEntrySw.TotalElapsed.TotalMilliseconds,
                                        (long) loadAndDeserializeSw.TotalElapsed.TotalMilliseconds);
                        }
                    }
                    else
                    {
                        // conflictingValue != null && !obsInput.Empty
                        //     ==>
                        //         if (conflictingValue is GraphDescriptor) return conflictingValue (weird case)
                        //         else
                        //             currentFingerprint = ##(currentFingerprint + conflictingValue)
                        //             obsInputs -= conflictingValue
                        switch (descriptor.Result.Kind)
                        {
                            case PipFingerprintEntryKind.GraphDescriptor:
                                return StorePipGraphCacheDescriptorResult.CreateForUnknown(
                                    (PipGraphCacheDescriptor) descriptor.Result.Deserialize(m_engineContext.CancellationToken, cacheQueryData),
                                    fingerprintChains,
                                    hopCount,
                                    sw.ElapsedMilliseconds,
                                    (long) hashPipGraphInputSw.TotalElapsed.TotalMilliseconds,
                                    (long) storeFingerprintEntrySw.TotalElapsed.TotalMilliseconds,
                                    (long) loadAndDeserializeSw.TotalElapsed.TotalMilliseconds);
                            case PipFingerprintEntryKind.GraphInputDescriptor:
                                var conflictingGraphInputDescriptor = (PipGraphInputDescriptor) descriptor.Result.Deserialize(m_engineContext.CancellationToken, cacheQueryData);
                                Possible<ContentFingerprint> possibleFingerprint;

                                using (hashPipGraphInputSw.Start())
                                {
                                    possibleFingerprint = TryHashPipGraphInputDescriptor(
                                        currentFingerprint,
                                        buildParametersForHashingGraphInput,
                                        mountsForHashingGraphInput,
                                        conflictingGraphInputDescriptor,
                                        ProviderContext.Store,
                                        hopCount,
                                        inputTracker,
                                        out var _);
                                }

                                if (!possibleFingerprint.Succeeded)
                                {
                                    return
                                        StorePipGraphCacheDescriptorResult.CreateForFailed(
                                            StorePipGraphCacheDescriptorResultKind.FailedHashPipGraphInputDescriptor,
                                            fingerprintChains,
                                            hopCount,
                                            possibleFingerprint.Failure.DescribeIncludingInnerFailures(),
                                            sw.ElapsedMilliseconds,
                                            (long) hashPipGraphInputSw.TotalElapsed.TotalMilliseconds,
                                            (long) storeFingerprintEntrySw.TotalElapsed.TotalMilliseconds,
                                            (long) loadAndDeserializeSw.TotalElapsed.TotalMilliseconds);
                                }

                                currentFingerprint = possibleFingerprint.Result;
                                observedGraphInputs =
                                    observedGraphInputs.Except(
                                        ObservedGraphInputs.FromPipGraphInputDescriptor(
                                            PathTable,
                                            conflictingGraphInputDescriptor));
                                observedGraphInputsToStore = observedGraphInputs.IsEmpty
                                    ? null
                                    : observedGraphInputs.ToPipGraphInputDescriptor(PathTable);
                                break;

                            default:
                                return
                                    StorePipGraphCacheDescriptorResult.CreateForFailed(
                                        StorePipGraphCacheDescriptorResultKind.UnexpectedFingerprintEntryKind,
                                        fingerprintChains,
                                        hopCount,
                                        descriptor.Result.Kind.ToString(),
                                        sw.ElapsedMilliseconds,
                                        (long) hashPipGraphInputSw.TotalElapsed.TotalMilliseconds,
                                        (long) storeFingerprintEntrySw.TotalElapsed.TotalMilliseconds,
                                        (long) loadAndDeserializeSw.TotalElapsed.TotalMilliseconds);
                        }
                    }
                }
                else
                {
                    if (!observedGraphInputs.IsEmpty)
                    {
                        // conflictingValue == null && !obsInputs.Empty
                        //     ==>
                        //         currentFingerprint = ##(currentFingerprint + obsInputs)
                        //         obsInputs = EmptyObsInputs
                        Possible<ContentFingerprint> possibleFingerprint;

                        using (hashPipGraphInputSw.Start())
                        {
                            possibleFingerprint = TryHashPipGraphInputDescriptor(
                                currentFingerprint,
                                buildParametersForHashingGraphInput,
                                mountsForHashingGraphInput,
                                observedGraphInputs.ToPipGraphInputDescriptor(PathTable),
                                ProviderContext.Store,
                                hopCount,
                                inputTracker,
                                out var _);
                        }

                        if (!possibleFingerprint.Succeeded)
                        {
                            return
                                StorePipGraphCacheDescriptorResult.CreateForFailed(
                                    StorePipGraphCacheDescriptorResultKind.FailedHashPipGraphInputDescriptor,
                                    fingerprintChains,
                                    hopCount,
                                    possibleFingerprint.Failure.DescribeIncludingInnerFailures(),
                                    sw.ElapsedMilliseconds,
                                    (long) hashPipGraphInputSw.TotalElapsed.TotalMilliseconds,
                                    (long) storeFingerprintEntrySw.TotalElapsed.TotalMilliseconds,
                                    (long) loadAndDeserializeSw.TotalElapsed.TotalMilliseconds);
                        }

                        currentFingerprint = possibleFingerprint.Result;
                        observedGraphInputs = ObservedGraphInputs.CreateEmpty(PathTable);
                        observedGraphInputsToStore = null;
                    }
                    else
                    {
                        // conflictingValue == null && obsInputs.Empty
                        //     ==>
                        //         graphDescriptor
                        return StorePipGraphCacheDescriptorResult.CreateForSuccess(
                            pipGraphCacheDescriptor,
                            fingerprintChains,
                            hopCount,
                            sw.ElapsedMilliseconds,
                            (long) hashPipGraphInputSw.TotalElapsed.TotalMilliseconds,
                            (long) storeFingerprintEntrySw.TotalElapsed.TotalMilliseconds,
                            (long) loadAndDeserializeSw.TotalElapsed.TotalMilliseconds);
                    }
                }
            }
        }

        private void LogStorePipGraphCacheDescriptorToCache(StorePipGraphCacheDescriptorResult result)
        {
            string fingerprintChain = result.Fingerprints != null && result.Fingerprints.Count > 0 
                ? string.Join(", ", result.Fingerprints.Select(f => f.ToString()))
                : string.Empty;

            Tracing.Logger.Log.StorePipGraphCacheDescriptorToCache(
                m_loggingContext,
                result.Kind.ToString(),
                result.HopCount,
                result.Reason,
                unchecked((int) result.ElapsedTimeMs),
                unchecked((int) result.HashingGraphInputsElapsedMs),
                unchecked((int) result.StoringFingerprintEntryElapsedMs),
                unchecked((int) result.LoadingAndDeserializingElapsedMs),
                fingerprintChain);
        }

        private Possible<ContentFingerprint> TryHashPipGraphInputDescriptor(
            ContentFingerprint ancestorFingerprint,
            BuildParameters.IBuildParameters buildParameters,
            IReadOnlyDictionary<string, IMount> mounts,
            PipGraphInputDescriptor pipGraphInputDescriptor,
            ProviderContext providerContext,
            int hop,
            InputTracker inputTracker,
            out MismatchedInputCollection possibleMismatchedInputs)
        {
            Contract.Requires(buildParameters != null);
            Contract.Requires(mounts != null);
            Contract.Requires(pipGraphInputDescriptor != null);

            possibleMismatchedInputs = new MismatchedInputCollection();

            int pathInputDifferenceCount = 0;
            int environmentInputDifferenceCount = 0;
            int mountInputDifferenceCount = 0;

            ContentFingerprint graphInputFingerprint;

            using (var hasher = new CoreHashingHelper(false))
            {
                hasher.Add("AncestorChainFingerprint", ancestorFingerprint.Hash);

                using (var pathObservationHasher = new CoreHashingHelper(false))
                {
                    int failedHashCount = 0;
                    var contentHashes = new ContentHash[pipGraphInputDescriptor.ObservedInputsSortedByPath.Count];
                    var failedContentHashes = new bool[pipGraphInputDescriptor.ObservedInputsSortedByPath.Count];

                    Parallel.For(
                        0,
                        pipGraphInputDescriptor.ObservedInputsSortedByPath.Count,
                        new ParallelOptions { MaxDegreeOfParallelism = m_maxDegreeOfParallelism },
                        index =>
                        {
                            var pathAndObservation = pipGraphInputDescriptor.ObservedInputsSortedByPath[index];

                            if (pathAndObservation.ObservedInputKind == ObservedInputKind.ObservedInput)
                            {
                                var possibleHash = TryGetAndRecordContentHash(pathAndObservation.StringKeyedHash, inputTracker);

                                if (possibleHash.Succeeded)
                                {
                                    contentHashes[index] = possibleHash.Result;
                                    failedContentHashes[index] = false;
                                }
                                else
                                {
                                    failedContentHashes[index] = true;

                                    if (Interlocked.Increment(ref failedHashCount) < FailedHashLimit)
                                    {
                                        Tracing.Logger.Log.FailedHashingGraphFileInput(
                                            m_loggingContext,
                                            providerContext.ToString(),
                                            hop,
                                            pathAndObservation.StringKeyedHash.Key,
                                            possibleHash.Failure.Describe());
                                    }
                                }
                            }
                            else
                            {
                                Contract.Assert(pathAndObservation.ObservedInputKind == ObservedInputKind.DirectoryMembership);

                                var possibleDirectoryFingerprint = TryComputeDirectoryMembershipFingerprint(
                                    pathAndObservation.StringKeyedHash.Key,
                                    inputTracker);

                                if (possibleDirectoryFingerprint.Succeeded)
                                {
                                    contentHashes[index] = possibleDirectoryFingerprint.Result.Hash;
                                    failedContentHashes[index] = false;
                                }
                                else
                                {
                                    failedContentHashes[index] = true;

                                    if (Interlocked.Increment(ref failedHashCount) < FailedHashLimit)
                                    {
                                        Tracing.Logger.Log.FailedComputingFingerprintGraphDirectoryInput(
                                            m_loggingContext,
                                            providerContext.ToString(),
                                            hop,
                                            pathAndObservation.StringKeyedHash.Key,
                                            possibleDirectoryFingerprint.Failure.Describe());
                                    }
                                }
                            }
                        });

                    for (int i = 0; i < pipGraphInputDescriptor.ObservedInputsSortedByPath.Count; ++i)
                    {
                        if (failedContentHashes[i])
                        {
                            // Skip if content hash cannot be computed.
                            continue;
                        }

                        string path = pipGraphInputDescriptor.ObservedInputsSortedByPath[i].StringKeyedHash.Key.ToUpperInvariant();
                        ContentHash hash = pipGraphInputDescriptor.ObservedInputsSortedByPath[i].StringKeyedHash.ContentHash.ToContentHash();
                        string kind = pipGraphInputDescriptor.ObservedInputsSortedByPath[i].ObservedInputKind.ToString();

                        pathObservationHasher.Add(path + ":" + kind, contentHashes[i]);

                        if (contentHashes[i] != hash && pathInputDifferenceCount++ < InputDifferencesLimit)
                        {
                            var mismatch = new MismatchedObservedInput(
                                path,
                                pipGraphInputDescriptor.ObservedInputsSortedByPath[i].ObservedInputKind, 
                                hash, 
                                contentHashes[i]);
                            possibleMismatchedInputs.Add(mismatch);

                            Tracing.Logger.Log.MismatchInputInGraphInputDescriptor(
                                m_loggingContext,
                                providerContext.ToString(),
                                hop,
                                mismatch.ToString());
                        }
                    }

                    hasher.Add("PathObservations", pathObservationHasher.GenerateHash());
                }

                using (var environmentVariableHasher = new CoreHashingHelper(false))
                {
                    foreach (var environmentVariable in pipGraphInputDescriptor.EnvironmentVariablesSortedByName)
                    {
                        HashEnvironmentVariable(
                            providerContext,
                            hop,
                            buildParameters,
                            environmentVariableHasher,
                            environmentVariable.Key,
                            environmentVariable.Value,
                            possibleMismatchedInputs,
                            ref environmentInputDifferenceCount);
                    }

                    hasher.Add("EnvironmentVariables", environmentVariableHasher.GenerateHash());
                }

                using (var mountHasher = new CoreHashingHelper(false))
                {
                    foreach (var mount in pipGraphInputDescriptor.MountsSortedByName)
                    {
                        HashMount(
                            providerContext,
                            hop,
                            mounts,
                            mountHasher,
                            mount.Key,
                            mount.Value,
                            possibleMismatchedInputs,
                            ref mountInputDifferenceCount);
                    }

                    hasher.Add("Mounts", mountHasher.GenerateHash());
                }

                graphInputFingerprint = new ContentFingerprint(hasher.GenerateHash());
            }

            return graphInputFingerprint;
        }

        private Possible<ContentHash> TryGetAndRecordContentHash(StringKeyedHash stringKeyedHash, InputTracker inputTracker)
        {
            string path = stringKeyedHash.Key;

            Contract.Requires(!string.IsNullOrWhiteSpace(path));

            if (inputTracker != null && inputTracker.TryGetHashForKnownInputFile(path, out ContentHash contentHash))
            {
                return contentHash;
            }

            try
            {
                if (!File.Exists(path))
                {
                    return WellKnownContentHashes.AbsentFile;
                }

                // If the cached hash is just a marker for an existent probe, then as soon as we know the file
                // is present, we should just return WellKnownContentHashes.ExistentFileProbe. This also avoids
                // potentially hashing the file
                if (stringKeyedHash.ContentHash.ToContentHash() == WellKnownContentHashes.ExistentFileProbe)
                {
                    return WellKnownContentHashes.ExistentFileProbe;
                }

                var identityAndContentInfo =
                    m_fileContentTable.GetAndRecordContentHashAsync(path)
                    .GetAwaiter()
                    .GetResult()
                    .VersionedFileIdentityAndContentInfo;

                return identityAndContentInfo.FileContentInfo.Hash;
            }
            catch (BuildXLException e)
            {
                return new Failure<string>(e.Message);
            }
        }

        private void HashEnvironmentVariable(
            ProviderContext providerContext,
            int hop,
            BuildParameters.IBuildParameters buildParameters,
            CoreHashingHelperBase hasher,
            string name,
            string comparedValue,
            MismatchedInputCollection possibleMismatchedInputs,
            ref int environmentInputDifferenceCount)
        {
            Contract.Requires(buildParameters != null);
            Contract.Requires(hasher != null);
            Contract.Requires(!string.IsNullOrWhiteSpace(name));

            name = name.ToCanonicalizedEnvVar();
            comparedValue = InputTracker.NormalizeEnvironmentVariableValue(comparedValue).ToUpperInvariant();
            string value = InputTracker.NormalizeEnvironmentVariableValue(buildParameters.ContainsKey(name) ? buildParameters[name] : null).ToUpperInvariant();

            hasher.Add(name, value);

            if (!string.Equals(comparedValue, value, StringComparison.OrdinalIgnoreCase) && ++environmentInputDifferenceCount < InputDifferencesLimit)
            {
                var mismatch = new MismatchedEnvironmentVariableInput(name, comparedValue, value);
                possibleMismatchedInputs.Add(mismatch);
                Tracing.Logger.Log.MismatchInputInGraphInputDescriptor(m_loggingContext, providerContext.ToString(), hop, mismatch.ToString());
            }
        }

        private void HashMount(
            ProviderContext providerContext,
            int hop,
            IReadOnlyDictionary<string, IMount> mounts,
            CoreHashingHelperBase hasher,
            string name,
            string comparedValue,
            MismatchedInputCollection possibleMismatchedInputs,
            ref int mountInputDifferenceCount)
        {
            Contract.Requires(mounts != null);
            Contract.Requires(hasher != null);
            Contract.Requires(!string.IsNullOrWhiteSpace(name));

            name = name.ToUpperInvariant();
            comparedValue = comparedValue != null ? comparedValue.ToCanonicalizedPath() : NullPathMarker;
            string value = mounts.TryGetValue(name, out IMount mountValue) && mountValue != null ? mountValue.Path.ToString(PathTable).ToCanonicalizedPath() : NullPathMarker;

            hasher.Add(name, value);

            if (!string.Equals(comparedValue, value, OperatingSystemHelper.PathComparison) && ++mountInputDifferenceCount < InputDifferencesLimit)
            {
                var mismatch = new MismatchedMountInput(name, comparedValue, value);
                Tracing.Logger.Log.MismatchInputInGraphInputDescriptor(m_loggingContext, providerContext.ToString(), hop, mismatch.ToString());
            }
        }

        private static Possible<DirectoryMembershipTrackingFingerprint> TryComputeDirectoryMembershipFingerprint(
            string directoryPath,
            InputTracker inputTracker)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(directoryPath));

            return inputTracker?.TryComputeDirectoryMembershipFingerprint(directoryPath) ??
                   InputTracker.TryComputeDirectoryMembershipFingerprint(directoryPath, null);
        }

        private ObservedGraphInputs CreateObservedGraphInputs(
            InputTracker tracker, 
            BuildParameters.IBuildParameters buildParameters,
            IReadOnlyDictionary<string, IMount> mounts)
        {
            var inputFiles = tracker.InputHashes.Select(kvp => new GraphPathInput(AbsolutePath.Create(PathTable, kvp.Key), kvp.Value, false));
            var inputDirectories = tracker.DirectoryFingerprints.Select(kvp => new GraphPathInput(AbsolutePath.Create(PathTable, kvp.Key), kvp.Value.Hash, true));
            var inputPaths = SortedReadOnlyArray<GraphPathInput, GraphPathInput.ByPathAndKindComparer>.SortUnsafe(
                inputFiles.Concat(inputDirectories).ToArray(),
                new GraphPathInput.ByPathAndKindComparer(PathTable.ExpandedPathComparer));

            var inputEnvironmentVariables =
                SortedReadOnlyArray<EnvironmentVariableInput, EnvironmentVariableInput.ByNameComparer>.SortUnsafe(
                    buildParameters.ToDictionary().Select(kvp => new EnvironmentVariableInput(kvp.Key, kvp.Value)).ToArray(),
                    EnvironmentVariableInput.ByNameComparer.Instance);

            var inputMounts = SortedReadOnlyArray<MountInput, MountInput.ByNameComparer>.SortUnsafe(
                mounts.Select(kvp => new MountInput(kvp.Key, kvp.Value != null ? kvp.Value.Path : AbsolutePath.Invalid)).ToArray(),
                MountInput.ByNameComparer.Instance);

            return new ObservedGraphInputs(inputPaths, inputEnvironmentVariables, inputMounts);
        }

        private enum ProviderContext
        {
            /// <summary>
            /// Getting graph descriptor for compatible graph.
            /// </summary>
            CompatibleGet,

            /// <summary>
            /// Getting graph descriptor for exact graph.
            /// </summary>
            ExactGet,

            /// <summary>
            /// Storing graph descriptor.
            /// </summary>
            Store
        }

        /// <summary>
        /// Base class for mismatched graph input.
        /// </summary>
        internal abstract class MismatchedInput
        {
        }

        /// <summary>
        /// Class recording mismatched observed input file or observed enumeration.
        /// </summary>
        internal class MismatchedObservedInput : MismatchedInput
        { 
            /// <summary>
            /// Path to input file or to enumerated directory.
            /// </summary>
            public readonly string Path;

            /// <summary>
            /// Observation kind.
            /// </summary>
            public readonly ObservedInputKind Kind;

            /// <summary>
            /// Expected content hash.
            /// </summary>
            public readonly ContentHash ExpectedHash;

            /// <summary>
            /// Actual content hash.
            /// </summary>
            public readonly ContentHash ActualHash;

            /// <summary>
            /// Creates an instance of <see cref="MismatchedObservedInput"/>.
            /// </summary>
            /// <param name="path">Path to input file or enumerated directory.</param>
            /// <param name="kind">Observation kind.</param>
            /// <param name="expectedHash">Expected hash.</param>
            /// <param name="actualHash">Actual hash.</param>
            public MismatchedObservedInput(string path, ObservedInputKind kind, ContentHash expectedHash, ContentHash actualHash)
            {
                Path = path;
                Kind = kind;
                ExpectedHash = expectedHash;
                ActualHash = actualHash;
            }

            /// <inheritdoc />
            public override string ToString() => I($"Mismatched observed input: '{Path}' ({Kind}) | Expected hash: {ExpectedHash} | Actual hash: {ActualHash}");
        }

        /// <summary>
        /// Class recording mismatched input environment variable.
        /// </summary>
        internal class MismatchedEnvironmentVariableInput : MismatchedInput
        {
            /// <summary>
            /// Environment variable name.
            /// </summary>
            public readonly string Name;

            /// <summary>
            /// Expected value.
            /// </summary>
            public readonly string ExpectedValue;

            /// <summary>
            /// Actual value.
            /// </summary>
            public readonly string ActualValue;

            /// <summary>
            /// Creates an instance of <see cref="MismatchedEnvironmentVariableInput"/>.
            /// </summary>
            /// <param name="name">Environment variable name.</param>
            /// <param name="expectedValue">Expected value.</param>
            /// <param name="actualValue">Actual value.</param>
            public MismatchedEnvironmentVariableInput(string name, string expectedValue, string actualValue)
            {
                Name = name;
                ExpectedValue = expectedValue;
                ActualValue = actualValue;
            }

            /// <inheritdoc />
            public override string ToString() => I($"Mismatched environment variable: '{Name}' | Expected value: '{ExpectedValue}' | Actual value: '{ActualValue}'");
        }

        /// <summary>
        /// Class recording mismatched input mounts.
        /// </summary>
        internal class MismatchedMountInput : MismatchedInput
        {
            /// <summary>
            /// Mount name.
            /// </summary>
            public readonly string Name;

            /// <summary>
            /// Expected path.
            /// </summary>
            public readonly string ExpectedPath;

            /// <summary>
            /// Actual path.
            /// </summary>
            public readonly string ActualPath;

            /// <summary>
            /// Creates an instance of <see cref="MismatchedMountInput"/>.
            /// </summary>
            /// <param name="name">Mount name.</param>
            /// <param name="expectedPath">Expected path.</param>
            /// <param name="actualPath">Actual path.</param>
            public MismatchedMountInput(string name, string expectedPath, string actualPath)
            {
                Name = name;
                ExpectedPath = expectedPath;
                ActualPath = actualPath;
            }

            /// <inheritdoc />
            public override string ToString() => I($"Mismatched mount: '{Name}' | Expected path: '{ExpectedPath}' | Actual path: '{ActualPath}'");
        }

        /// <summary>
        /// Class for collecting mismatched graph inputs.
        /// </summary>
        internal class MismatchedInputCollection
        {
            private const int MaxEntryKindCount = 10;
            private int m_mismatchedObservedInputCount = 0;
            private int m_mismatchedEnvironmentVariableCount = 0;
            private int m_mismatchedMountCount = 0;
            private readonly List<MismatchedInput> m_mismatchedInputs = new List<MismatchedInput>(MaxEntryKindCount * 3);

            /// <summary>
            /// True if there is a mismatched input.
            /// </summary>
            public bool HasMismatch => m_mismatchedInputs.Count > 0;

            /// <summary>
            /// Adds a mismatched input into this collection.
            /// </summary>
            /// <param name="mismatchedInput">An instance of <see cref="MismatchedInput"/>.</param>
            public void Add(MismatchedInput mismatchedInput)
            {
                if (m_mismatchedInputs.Count == m_mismatchedInputs.Capacity)
                {
                    return;
                }

                int kindCount = 0;

                switch (mismatchedInput)
                {
                    case MismatchedObservedInput _:
                        kindCount = m_mismatchedObservedInputCount++;
                        break;
                    case MismatchedEnvironmentVariableInput _:
                        kindCount = m_mismatchedEnvironmentVariableCount++;
                        break;
                    case MismatchedMountInput _:
                        kindCount = m_mismatchedMountCount++;
                        break;
                }

                if (kindCount < MaxEntryKindCount)
                {
                    m_mismatchedInputs.Add(mismatchedInput);
                }
            }

            /// <inheritdoc />
            public override string ToString() => ToString(string.Empty);

            /// <summary>
            /// Gets string representation with a prefix for each mismatch entry.
            /// </summary>
            public string ToString(string prefix) => string.Join(Environment.NewLine, m_mismatchedInputs.Select(m => prefix + m.ToString()));
        }
    }

    /// <summary>
    /// Result kind of looking up the cache for <see cref="PipGraphCacheDescriptor"/>.
    /// </summary>
    internal enum GetPipGraphCacheDescriptorResultKind
    {
        /// <summary>
        /// No cache look-up.
        /// </summary>
        None,

        /// <summary>
        /// Cache hit.
        /// </summary>
        Hit,

        /// <summary>
        /// Cache miss.
        /// </summary>
        Miss,

        /// <summary>
        /// Failed to get <see cref="PipFingerprintEntry"/>.
        /// </summary>
        FailedGetFingerprintEntry,

        /// <summary>
        /// Failed to hash <see cref="PipGraphInputDescriptor"/>.
        /// </summary>
        FailedHashPipGraphInputDescriptor,

        /// <summary>
        /// Unexpected <see cref="PipFingerprintEntryKind"/>.
        /// </summary>
        UnexpectedFingerprintEntryKind,

        /// <summary>
        /// Exceeded max hop count.
        /// </summary>
        ExceededMaxHopCount,
    }

    /// <summary>
    /// Extension for <see cref="GetPipGraphCacheDescriptorResultKind"/>.
    /// </summary>
    internal static class GetPipGraphCacheDescriptorResultKindExtension
    {
        /// <summary>
        /// Checks if the result of looking up <see cref="PipGraphCacheDescriptor"/> on the cache is a failure.
        /// </summary>
        public static bool IsFailure(this GetPipGraphCacheDescriptorResultKind kind) =>
            kind == GetPipGraphCacheDescriptorResultKind.FailedGetFingerprintEntry ||
            kind == GetPipGraphCacheDescriptorResultKind.FailedHashPipGraphInputDescriptor ||
            kind == GetPipGraphCacheDescriptorResultKind.UnexpectedFingerprintEntryKind ||
            kind == GetPipGraphCacheDescriptorResultKind.ExceededMaxHopCount;
    }

    /// <summary>
    /// Result of looking up the cache for <see cref="PipGraphCacheDescriptor"/>.
    /// </summary>
    internal class GetPipGraphCacheDescriptorResult
    {
        /// <summary>
        /// Resulting <see cref="PipGraphCacheDescriptor"/> if cache hit.
        /// </summary>
        public readonly PipGraphCacheDescriptor PipGraphCacheDescriptor;

        /// <summary>
        /// Result kind of looking up the cache for <see cref="PipGraphCacheDescriptor"/>.
        /// </summary>
        public readonly GetPipGraphCacheDescriptorResultKind Kind;

        /// <summary>
        /// Number of hops.
        /// </summary>
        public readonly int HopCount;

        /// <summary>
        /// Reason for failure.
        /// </summary>
        public readonly string Reason;

        /// <summary>
        /// Elapsed time.
        /// </summary>
        public readonly long ElapsedTimeMs;

        /// <summary>
        /// Elapsed time for hashing graph inputs.
        /// </summary>
        public readonly long HashGraphInputsElapsedTimeMs;

        /// <summary>
        /// Elapsed time for getting fingerprint entries from cache.
        /// </summary>
        public readonly long GetFingerprintEntryElapsedTimeMs;

        /// <summary>
        /// Chains of fingerprints.
        /// </summary>
        public readonly IReadOnlyList<ContentFingerprint> Fingerprints;

        /// <summary>
        /// Last recently mismatched inputs.
        /// </summary>
        public readonly CachedGraphProvider.MismatchedInputCollection LastRecentlyMismatchedInputs;

        /// <summary>
        /// True if the result is cache hit.
        /// </summary>
        public bool IsHit => Kind == GetPipGraphCacheDescriptorResultKind.Hit;

        private GetPipGraphCacheDescriptorResult(
            PipGraphCacheDescriptor pipGraphCacheDescriptor,
            GetPipGraphCacheDescriptorResultKind kind,
            IReadOnlyList<ContentFingerprint> fingerprints,
            CachedGraphProvider.MismatchedInputCollection lastRecentlyMismatchedInputs,
            int hopCount,
            string reason,
            long elapsedTimeMs,
            long hashGraphInputsElapsedTimeMs,
            long getFingerprintEntryElapsedTimeMs)
        {
            PipGraphCacheDescriptor = pipGraphCacheDescriptor;
            Kind = kind;
            Fingerprints = fingerprints;
            LastRecentlyMismatchedInputs = lastRecentlyMismatchedInputs;
            HopCount = hopCount;
            Reason = reason;
            ElapsedTimeMs = elapsedTimeMs;
            HashGraphInputsElapsedTimeMs = hashGraphInputsElapsedTimeMs;
            GetFingerprintEntryElapsedTimeMs = getFingerprintEntryElapsedTimeMs;
        }

        /// <summary>
        /// Creates an instance of <see cref="GetPipGraphCacheDescriptorResult"/> for cache hit case.
        /// </summary>
        public static GetPipGraphCacheDescriptorResult CreateForHit(
            PipGraphCacheDescriptor pipGraphCacheDescriptor,
            IReadOnlyList<ContentFingerprint> fingerprints,
            int hopCount,
            long elapsedTimeMs,
            long hashGraphInputsElapsedTimeMs,
            long getFingerprintEntryElapsedTimeMs)
        {
            Contract.Requires(pipGraphCacheDescriptor != null);

            return new GetPipGraphCacheDescriptorResult(
                pipGraphCacheDescriptor,
                GetPipGraphCacheDescriptorResultKind.Hit,
                fingerprints,
                null,
                hopCount,
                string.Empty,
                elapsedTimeMs,
                hashGraphInputsElapsedTimeMs,
                getFingerprintEntryElapsedTimeMs);
        }

        /// <summary>
        /// Creates an instance of <see cref="GetPipGraphCacheDescriptorResult"/> for cache miss case.
        /// </summary>
        public static GetPipGraphCacheDescriptorResult CreateForMiss(
            IReadOnlyList<ContentFingerprint> fingerprints,
            CachedGraphProvider.MismatchedInputCollection lastRecentlyMismatchedInputs,
            int hopCount,
            long elapsedTimeMs,
            long hashGraphInputsElapsedTimeMs,
            long getFingerprintEntryElapsedTimeMs)
        {
            return new GetPipGraphCacheDescriptorResult(
                null,
                GetPipGraphCacheDescriptorResultKind.Miss,
                fingerprints,
                lastRecentlyMismatchedInputs,
                hopCount,
                string.Empty,
                elapsedTimeMs,
                hashGraphInputsElapsedTimeMs,
                getFingerprintEntryElapsedTimeMs);
        }

        /// <summary>
        /// Creates an instance of <see cref="GetPipGraphCacheDescriptorResult"/> for no cache look-up.
        /// </summary>
        public static GetPipGraphCacheDescriptorResult CreateForNone()
        {
            return new GetPipGraphCacheDescriptorResult(
                null,
                GetPipGraphCacheDescriptorResultKind.None,
                new List<ContentFingerprint>(0),
                null,
                0,
                string.Empty,
                0,
                0,
                0);
        }

        /// <summary>
        /// Creates an instance of <see cref="GetPipGraphCacheDescriptorResult"/> for failure.
        /// </summary>
        public static GetPipGraphCacheDescriptorResult CreateForFailed(
            GetPipGraphCacheDescriptorResultKind kind,
            IReadOnlyList<ContentFingerprint> fingerprints,
            int hopCount,
            string reason,
            long elapsedTimeMs,
            long hashGraphInputsElapsedTimeMs,
            long getFingerprintEntryElapsedTimeMs)
        {
            Contract.Requires(kind.IsFailure());
            return new GetPipGraphCacheDescriptorResult(
                null,
                kind,
                fingerprints,
                null,
                hopCount,
                reason ?? string.Empty,
                elapsedTimeMs,
                hashGraphInputsElapsedTimeMs,
                getFingerprintEntryElapsedTimeMs);
        }
    }

    /// <summary>
    /// Result kind of storing <see cref="PipGraphCacheDescriptor"/> to the cache.
    /// </summary>
    internal enum StorePipGraphCacheDescriptorResultKind
    {
        /// <summary>
        /// Success.
        /// </summary>
        Success,

        /// <summary>
        /// Conflict with other entries.
        /// </summary>
        Conflict,

        /// <summary>
        /// Unknown result (weird case).
        /// </summary>
        Unknown,

        /// <summary>
        /// Failed to store <see cref="PipFingerprintEntry"/>.
        /// </summary>
        FailedStoreFingerprintEntry,

        /// <summary>
        /// Failed to hash <see cref="PipGraphInputDescriptor"/>.
        /// </summary>
        FailedHashPipGraphInputDescriptor,

        /// <summary>
        /// Failed to load and deserialize content of <see cref="CacheEntry"/>.
        /// </summary>
        FailedLoadAndDeserializeContent,

        /// <summary>
        /// Unexpected <see cref="PipFingerprintEntryKind"/>.
        /// </summary>
        UnexpectedFingerprintEntryKind,

        /// <summary>
        /// Exceeded max hop count.
        /// </summary>
        ExceededMaxHopCount,
    }

    /// <summary>
    /// Extension for <see cref="StorePipGraphCacheDescriptorResultKind"/>.
    /// </summary>
    internal static class StorePipGraphCacheDescriptorResultKindExtension
    {
        /// <summary>
        /// Checks if the result of storing <see cref="PipGraphCacheDescriptor"/> to the cache is a failure.
        /// </summary>
        public static bool IsFailure(this StorePipGraphCacheDescriptorResultKind kind) =>
            kind == StorePipGraphCacheDescriptorResultKind.FailedLoadAndDeserializeContent ||
            kind == StorePipGraphCacheDescriptorResultKind.FailedStoreFingerprintEntry ||
            kind == StorePipGraphCacheDescriptorResultKind.FailedHashPipGraphInputDescriptor ||
            kind == StorePipGraphCacheDescriptorResultKind.UnexpectedFingerprintEntryKind ||
            kind == StorePipGraphCacheDescriptorResultKind.ExceededMaxHopCount;
    }

    /// <summary>
    /// Result of storing <see cref="PipGraphCacheDescriptor"/>.
    /// </summary>
    internal class StorePipGraphCacheDescriptorResult
    {
        /// <summary>
        /// Resulting <see cref="PipGraphCacheDescriptor"/> if successful or conflict.
        /// </summary>
        public readonly PipGraphCacheDescriptor PipGraphCacheDescriptor;

        /// <summary>
        /// Result kind of storing <see cref="PipGraphCacheDescriptor"/> to the cache.
        /// </summary>
        public readonly StorePipGraphCacheDescriptorResultKind Kind;

        /// <summary>
        /// Number of hops.
        /// </summary>
        public readonly int HopCount;

        /// <summary>
        /// Reason for failure.
        /// </summary>
        public readonly string Reason;

        /// <summary>
        /// Elapsed time.
        /// </summary>
        public readonly long ElapsedTimeMs;

        /// <summary>
        /// Elapsed time for hasing graph inputs.
        /// </summary>
        public readonly long HashingGraphInputsElapsedMs;

        /// <summary>
        /// Elapsed time for storing fingerprint entries.
        /// </summary>
        public readonly long StoringFingerprintEntryElapsedMs;

        /// <summary>
        /// Elapsed time for loading and deserializing metadata.
        /// </summary>
        public readonly long LoadingAndDeserializingElapsedMs;

        /// <summary>
        /// Chains of fingerprints.
        /// </summary>
        public readonly IReadOnlyList<ContentFingerprint> Fingerprints;

        private StorePipGraphCacheDescriptorResult(
            PipGraphCacheDescriptor pipGraphCacheDescriptor,
            StorePipGraphCacheDescriptorResultKind kind,
            IReadOnlyList<ContentFingerprint> fingerprints,
            int hopCount,
            string reason,
            long elapsedTimeMs,
            long hashingGraphInputsElapsedMs,
            long storingFingerprintEntryElapsedMs,
            long loadingAndDeserializingElapsedMs)
        {
            PipGraphCacheDescriptor = pipGraphCacheDescriptor;
            Kind = kind;
            Fingerprints = fingerprints;
            HopCount = hopCount;
            Reason = reason;
            ElapsedTimeMs = elapsedTimeMs;
            HashingGraphInputsElapsedMs = hashingGraphInputsElapsedMs;
            StoringFingerprintEntryElapsedMs = storingFingerprintEntryElapsedMs;
            LoadingAndDeserializingElapsedMs = loadingAndDeserializingElapsedMs;
        }

        /// <summary>
        /// Creates an instance of <see cref="StorePipGraphCacheDescriptorResult"/> for successful case.
        /// </summary>
        public static StorePipGraphCacheDescriptorResult CreateForSuccess(
            PipGraphCacheDescriptor pipGraphCacheDescriptor,
            IReadOnlyList<ContentFingerprint> fingerprints,
            int hopCount,
            long elapsedTimeMs,
            long hashingGraphInputsElapsedMs,
            long storingFingerprintEntryElapsedMs,
            long loadingAndDeserializingElapsedMs)
        {
            return new StorePipGraphCacheDescriptorResult(
                pipGraphCacheDescriptor,
                StorePipGraphCacheDescriptorResultKind.Success,
                fingerprints,
                hopCount,
                string.Empty,
                elapsedTimeMs,
                hashingGraphInputsElapsedMs,
                storingFingerprintEntryElapsedMs,
                loadingAndDeserializingElapsedMs);
        }

        /// <summary>
        /// Creates an instance of <see cref="StorePipGraphCacheDescriptorResult"/> for conflict case.
        /// </summary>
        public static StorePipGraphCacheDescriptorResult CreateForConflict(
            PipGraphCacheDescriptor pipGraphCacheDescriptor,
            IReadOnlyList<ContentFingerprint> fingerprints,
            int hopCount,
            long elapsedTimeMs,
            long hashingGraphInputsElapsedMs,
            long storingFingerprintEntryElapsedMs,
            long loadingAndDeserializingElapsedMs)
        {
            return new StorePipGraphCacheDescriptorResult(
                pipGraphCacheDescriptor,
                StorePipGraphCacheDescriptorResultKind.Conflict,
                fingerprints,
                hopCount,
                StorePipGraphCacheDescriptorResultKind.Conflict.ToString(),
                elapsedTimeMs,
                hashingGraphInputsElapsedMs,
                storingFingerprintEntryElapsedMs,
                loadingAndDeserializingElapsedMs);
        }

        /// <summary>
        /// Creates an instance of <see cref="StorePipGraphCacheDescriptorResult"/> for unknown case.
        /// </summary>
        public static StorePipGraphCacheDescriptorResult CreateForUnknown(
            PipGraphCacheDescriptor pipGraphCacheDescriptor,
            IReadOnlyList<ContentFingerprint> fingerprints,
            int hopCount,
            long elapsedTimeMs,
            long hashingGraphInputsElapsedMs,
            long storingFingerprintEntryElapsedMs,
            long loadingAndDeserializingElapsedMs)
        {
            return new StorePipGraphCacheDescriptorResult(
                pipGraphCacheDescriptor,
                StorePipGraphCacheDescriptorResultKind.Unknown,
                fingerprints,
                hopCount,
                StorePipGraphCacheDescriptorResultKind.Unknown.ToString(),
                elapsedTimeMs,
                hashingGraphInputsElapsedMs,
                storingFingerprintEntryElapsedMs,
                loadingAndDeserializingElapsedMs);
        }

        /// <summary>
        /// Creates an instance of <see cref="StorePipGraphCacheDescriptorResult"/> for failed case.
        /// </summary>
        public static StorePipGraphCacheDescriptorResult CreateForFailed(
            StorePipGraphCacheDescriptorResultKind kind,
            IReadOnlyList<ContentFingerprint> fingerprints,
            int hopCount,
            string reason,
            long elapsedTimeMs,
            long hashingGraphInputsElapsedMs,
            long storingFingerprintEntryElapsedMs,
            long loadingAndDeserializingElapsedMs)
        {
            Contract.Requires(kind.IsFailure());
            return new StorePipGraphCacheDescriptorResult(
                null,
                kind,
                fingerprints,
                hopCount,
                reason ?? string.Empty,
                elapsedTimeMs,
                hashingGraphInputsElapsedMs,
                storingFingerprintEntryElapsedMs,
                loadingAndDeserializingElapsedMs);
        }
    }
}
