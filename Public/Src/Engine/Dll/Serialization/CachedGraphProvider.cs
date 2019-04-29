// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;

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
                    mounts)).IsHit)
            {
                LogGetPipGraphDescriptorFromCache(compatibleResult, GetPipGraphCacheDescriptorResult.CreateForNone());
                return compatibleResult.PipGraphCacheDescriptor;
            }

            GetPipGraphCacheDescriptorResult exactResult = await TryGetPipGraphCacheDescriptorAsync(
                graphFingerprint.ExactFingerprint.OverallFingerprint,
                buildParameters,
                mounts);

            LogGetPipGraphDescriptorFromCache(compatibleResult, exactResult);

            return exactResult.IsHit ? exactResult.PipGraphCacheDescriptor : null;
        }

        private async Task<GetPipGraphCacheDescriptorResult> TryGetPipGraphCacheDescriptorAsync(
            ContentFingerprint graphFingerprint,
            BuildParameters.IBuildParameters buildParameters,
            IReadOnlyDictionary<string, IMount> mounts)
        {
            Contract.Requires(buildParameters != null);
            Contract.Requires(mounts != null);

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
                            (PipGraphCacheDescriptor) metadata.Deserialize(cacheQueryData),
                            fingerprintChains,
                            hopCount,
                            sw.ElapsedMilliseconds,
                            (long) hashPipGraphInputSw.TotalElapsed.TotalMilliseconds,
                            (long) getFingerprintEntrySw.TotalElapsed.TotalMilliseconds);

                    case PipFingerprintEntryKind.GraphInputDescriptor:
                        var pipGraphInputs = (PipGraphInputDescriptor) metadata.Deserialize(cacheQueryData);
                        Possible<ContentFingerprint> possibleFingerprint;

                        using (hashPipGraphInputSw.Start())
                        {
                            possibleFingerprint = TryHashPipGraphInputDescriptor(currentFingerprint, buildParameters, mounts, pipGraphInputs, hopCount, null);
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
            string compatibleFingerprintChain = compatible.Fingerprints != null && compatible.Fingerprints.Count > 0
                ? string.Join(Environment.NewLine, compatible.Fingerprints.Select(f => "\t\tFingerprint: " + f.ToString()))
                : string.Empty;

            string exactFingerprintChain = exact.Fingerprints != null && exact.Fingerprints.Count > 0
                ? string.Join(Environment.NewLine, exact.Fingerprints.Select(f => "\t\tFingerprint: " + f.ToString()))
                : string.Empty;

            Tracing.Logger.Log.GetPipGraphDescriptorFromCache(
                m_loggingContext,
                compatible.Kind.ToString(),
                compatible.HopCount,
                compatible.Reason,
                unchecked((int) compatible.ElapsedTimeMs),
                unchecked((int) compatible.HashGraphInputsElapsedTimeMs),
                unchecked((int) compatible.GetFingerprintEntryElapsedTimeMs),
                compatibleFingerprintChain,
                exact.Kind.ToString(),
                exact.HopCount,
                exact.Reason,
                unchecked((int) exact.ElapsedTimeMs),
                unchecked((int) exact.HashGraphInputsElapsedTimeMs),
                unchecked((int) exact.GetFingerprintEntryElapsedTimeMs),
                exactFingerprintChain);
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

            var storeResult = await TryStorePipGraphCacheDescriptorAsync(
                inputTracker,
                buildParametersImpactingBuild,
                mountsImpactingBuild,
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
            BuildParameters.IBuildParameters buildParametersImpactingBuild,
            IReadOnlyDictionary<string, IMount> mountsImpactingBuild,
            BuildParameters.IBuildParameters availableBuildParameters,
            IReadOnlyDictionary<string, IMount> availableMounts,
            ContentFingerprint graphFingerprint,
            ObservedGraphInputs observedGraphInputs,
            PipGraphCacheDescriptor pipGraphCacheDescriptor)
        {
            Contract.Requires(inputTracker != null);
            Contract.Requires(buildParametersImpactingBuild != null);
            Contract.Requires(mountsImpactingBuild != null);
            Contract.Requires(availableBuildParameters != null);
            Contract.Requires(availableMounts != null);
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

                    if (!descriptor.Succeeded)
                    {
                        return StorePipGraphCacheDescriptorResult.CreateForFailed(
                            StorePipGraphCacheDescriptorResultKind.FailedLoadAndDeserializeContent,
                            fingerprintChains,
                            hopCount,
                            descriptor.Failure.DescribeIncludingInnerFailures(),
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
                                        (PipGraphCacheDescriptor) descriptor.Result.Deserialize(cacheQueryData),
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
                                        availableBuildParameters,
                                        availableMounts,
                                        (PipGraphInputDescriptor) descriptor.Result.Deserialize(cacheQueryData),
                                        hopCount,
                                        inputTracker);
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
                                    (PipGraphCacheDescriptor) descriptor.Result.Deserialize(cacheQueryData),
                                    fingerprintChains,
                                    hopCount,
                                    sw.ElapsedMilliseconds,
                                    (long) hashPipGraphInputSw.TotalElapsed.TotalMilliseconds,
                                    (long) storeFingerprintEntrySw.TotalElapsed.TotalMilliseconds,
                                    (long) loadAndDeserializeSw.TotalElapsed.TotalMilliseconds);
                            case PipFingerprintEntryKind.GraphInputDescriptor:
                                var conflictingGraphInputDescriptor = (PipGraphInputDescriptor) descriptor.Result.Deserialize(cacheQueryData);
                                Possible<ContentFingerprint> possibleFingerprint;

                                using (hashPipGraphInputSw.Start())
                                {
                                    possibleFingerprint = TryHashPipGraphInputDescriptor(
                                        currentFingerprint,
                                        availableBuildParameters,
                                        availableMounts,
                                        conflictingGraphInputDescriptor,
                                        hopCount,
                                        inputTracker);
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
                                availableBuildParameters,
                                availableMounts,
                                observedGraphInputs.ToPipGraphInputDescriptor(PathTable),
                                hopCount,
                                inputTracker);
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
                ? string.Join(Environment.NewLine, result.Fingerprints.Select(f => "\tFingerprint: " + f.ToString()))
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
            int hop,
            InputTracker inputTracker)
        {
            Contract.Requires(buildParameters != null);
            Contract.Requires(mounts != null);
            Contract.Requires(pipGraphInputDescriptor != null);

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
                            Tracing.Logger.Log.MismatchPathInGraphInputDescriptor(
                                m_loggingContext,
                                hop,
                                path,
                                kind,
                                hash.ToString(),
                                contentHashes[i].ToString());
                        }
                    }

                    hasher.Add("PathObservations", pathObservationHasher.GenerateHash());
                }

                using (var environmentVariableHasher = new CoreHashingHelper(false))
                {
                    foreach (var environmentVariable in pipGraphInputDescriptor.EnvironmentVariablesSortedByName)
                    {
                        HashEnvironmentVariable(
                            hop,
                            buildParameters,
                            environmentVariableHasher,
                            environmentVariable.Key,
                            environmentVariable.Value,
                            ref environmentInputDifferenceCount);
                    }

                    hasher.Add("EnvironmentVariables", environmentVariableHasher.GenerateHash());
                }

                using (var mountHasher = new CoreHashingHelper(false))
                {
                    foreach (var mount in pipGraphInputDescriptor.MountsSortedByName)
                    {
                        HashMount(
                            hop,
                            mounts,
                            mountHasher,
                            mount.Key,
                            mount.Value,
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
            int hop,
            BuildParameters.IBuildParameters buildParameters,
            CoreHashingHelperBase hasher,
            string name,
            string comparedValue,
            ref int environmentInputDifferenceCount)
        {
            Contract.Requires(buildParameters != null);
            Contract.Requires(hasher != null);
            Contract.Requires(!string.IsNullOrWhiteSpace(name));

            name = name.ToUpperInvariant();
            comparedValue = InputTracker.NormalizeEnvironmentVariableValue(comparedValue).ToUpperInvariant();
            string value = InputTracker.NormalizeEnvironmentVariableValue(buildParameters.ContainsKey(name) ? buildParameters[name] : null).ToUpperInvariant();

            hasher.Add(name, value);

            if (!string.Equals(comparedValue, value, StringComparison.OrdinalIgnoreCase) && ++environmentInputDifferenceCount < InputDifferencesLimit)
            {
                Tracing.Logger.Log.MismatchEnvironmentInGraphInputDescriptor(m_loggingContext, hop, name, comparedValue, value);
            }
        }

        private void HashMount(
            int hop,
            IReadOnlyDictionary<string, IMount> mounts,
            CoreHashingHelperBase hasher,
            string name,
            string comparedValue,
            ref int mountInputDifferenceCount)
        {
            Contract.Requires(mounts != null);
            Contract.Requires(hasher != null);
            Contract.Requires(!string.IsNullOrWhiteSpace(name));

            name = name.ToUpperInvariant();
            comparedValue = comparedValue != null ? comparedValue.ToUpperInvariant() : NullPathMarker;
            string value = mounts.TryGetValue(name, out IMount mountValue) && mountValue != null ? mountValue.Path.ToString(PathTable).ToUpperInvariant() : NullPathMarker;

            hasher.Add(name, value);

            if (!string.Equals(comparedValue, value, StringComparison.OrdinalIgnoreCase) && ++mountInputDifferenceCount < InputDifferencesLimit)
            {
                Tracing.Logger.Log.MismatchMountInGraphInputDescriptor(m_loggingContext, hop, name, comparedValue, value);
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
        /// True if the result is cache hit.
        /// </summary>
        public bool IsHit => Kind == GetPipGraphCacheDescriptorResultKind.Hit;

        private GetPipGraphCacheDescriptorResult(
            PipGraphCacheDescriptor pipGraphCacheDescriptor,
            GetPipGraphCacheDescriptorResultKind kind,
            IReadOnlyList<ContentFingerprint> fingerprints,
            int hopCount,
            string reason,
            long elapsedTimeMs,
            long hashGraphInputsElapsedTimeMs,
            long getFingerprintEntryElapsedTimeMs)
        {
            PipGraphCacheDescriptor = pipGraphCacheDescriptor;
            Kind = kind;
            Fingerprints = fingerprints;
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
            int hopCount,
            long elapsedTimeMs,
            long hashGraphInputsElapsedTimeMs,
            long getFingerprintEntryElapsedTimeMs)
        {
            return new GetPipGraphCacheDescriptorResult(
                null,
                GetPipGraphCacheDescriptorResultKind.Miss,
                fingerprints,
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
