// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.Engine;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Scheduler.Tracing;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Execution.Analyzer.Analyzers.CacheMiss
{
    /// <summary>
    /// Model for converting and populating a target graph <see cref="AnalysisModel"/> to match
    /// paths and other data from a given source graph
    /// </summary>
    internal sealed class ConversionModel
    {
        public readonly CachedGraph NewGraph;
        public readonly CachedGraph OldGraph;
        public readonly AnalysisModel ConvertedNewModel;
        public readonly AnalysisModel OldModel;

        public bool AreGraphsSame => OldGraph == NewGraph;

        private readonly ConcurrentBigMap<AbsolutePath, AbsolutePath> m_pathMap = new ConcurrentBigMap<AbsolutePath, AbsolutePath>();

        public ConversionModel(CachedGraph oldGraph, CachedGraph newGraph, AnalysisModel oldModel)
        {
            OldGraph = oldGraph;
            OldModel = oldModel;

            NewGraph = newGraph;
            ConvertedNewModel = new AnalysisModel(NewGraph)
            {
                LookupHashFunction = ConvertedLookupHash,
            };
        }

        private FileContentInfo ConvertedLookupHash(uint workerId, FileArtifact artifact)
        {
            return ConvertedNewModel.LookupHash(workerId, Convert(artifact));
        }

        public PipCachingInfo GetPipInfo(Process process)
        {
            var pipInfo = GetPipInfo(process.PipId);
            if (pipInfo != null && pipInfo.CacheablePipInfo == null)
            {
                pipInfo.CacheablePipInfo = CreateCacheablePipInfo(process);
            }

            return pipInfo;
        }

        private CacheablePipInfo CreateCacheablePipInfo(Process process)
        {
            return new CacheablePipInfo(
                process,
                OldGraph.Context,
                Convert(process.FileOutputs, this, (i, me) => me.Convert(i)),
                Convert(process.Dependencies, this, (i, me) => me.Convert(i)),
                Convert(process.DirectoryOutputs, this, (i, me) => me.Convert(i)),
                Convert(process.DirectoryDependencies, this, (i, me) => me.Convert(i)));
        }

        public PipCachingInfo GetPipInfo(PipId pipId)
        {
            var result = ConvertedNewModel.PipInfoMap.GetOrAdd(Convert(pipId), this, (pipId1, this1) => pipId.IsValid ?
                new PipCachingInfo(pipId1, this1.ConvertedNewModel) : null);
            var info = result.Item.Value;
            if (!result.IsFound)
            {
                info.OriginalPipId = pipId;
            }

            return info;
        }

        #region Execution Log Events

        /*
         * The methods below are execution log events which populate the analysis model
         * based on converted data for the events
         */

        public void FileArtifactContentDecided(uint workerId, FileArtifactContentDecidedEventData data)
        {
            var convertedFileArtifact = Convert(data.FileArtifact);
            if (!convertedFileArtifact.Path.IsValid)
            {
                return;
            }

            ConvertedNewModel.AddFileContentInfo(workerId, convertedFileArtifact, data.FileContentInfo);
        }

        public void DirectoryMembershipHashed(uint workerId, DirectoryMembershipHashedEventData data)
        {
            var pipId = Convert(data.PipId);
            if (pipId.IsValid != data.PipId.IsValid)
            {
                return;
            }

            data = Convert(data);

            ConvertedNewModel.AddDirectoryData(workerId, data);
        }

        private DirectoryMembershipHashedEventData Convert(DirectoryMembershipHashedEventData data)
        {
            if (AreGraphsSame)
            {
                return data;
            }

            data.Directory = Convert(data.Directory);
            data.PipId = Convert(data.PipId);
            data.Members = Convert(data.Members);

            return data;
        }

        public void ProcessFingerprintComputed(uint workerId, ProcessFingerprintComputationEventData data)
        {
            var pipInfo = GetPipInfo(data.PipId);
            if (pipInfo != null)
            {
                pipInfo.SetFingerprintComputation(Convert(pipInfo, data), workerId);
            }
        }

        #endregion

        #region Conversion Functions

        /*
         * The functions in this region convert data types in target graph to corresponding values in the
         * the target graph. For instance, AbsolutePaths are converted to AbsolutePaths corresponding to the
         * target graphs PathTable so equality comparisons work.
         */

        private ReadOnlyArray<T> Convert<T, TData>(ReadOnlyArray<T> array, TData data, Func<T, TData, T> convert)
        {
            if (AreGraphsSame)
            {
                return array;
            }

            if (!array.IsValid || array.Length == 0)
            {
                return array;
            }

            var converted = new T[array.Length];
            for (int i = 0; i < converted.Length; i++)
            {
                converted[i] = convert(array[i], data);
            }

            return ReadOnlyArray<T>.FromWithoutCopy(converted);
        }

        private List<AbsolutePath> Convert(List<AbsolutePath> members)
        {
            if (AreGraphsSame)
            {
                return members;
            }

            for (int i = 0; i < members.Count; i++)
            {
                members[i] = Convert(members[i]);
            }

            return members;
        }

        private ProcessFingerprintComputationEventData Convert(PipCachingInfo pipInfo, ProcessFingerprintComputationEventData data)
        {
            if (AreGraphsSame)
            {
                return data;
            }

            data.PipId = pipInfo.PipId;

            // Assuming that under the hood the computations are a mutable list or array
            var computations = (IList<ProcessStrongFingerprintComputationData>)data.StrongFingerprintComputations;
            for (int i = 0; i < computations.Count; i++)
            {
                computations[i] = Convert(computations[i]);
            }

            return data;
        }

        private ProcessStrongFingerprintComputationData Convert(ProcessStrongFingerprintComputationData computation)
        {
            if (AreGraphsSame)
            {
                return computation;
            }

            var pathSet = new ObservedPathSet(
                SortedReadOnlyArray<ObservedPathEntry, ObservedPathEntryExpandedPathComparer>.FromSortedArrayUnsafe(
                    Convert(computation.PathEntries, this, (i, me) => me.Convert(i)),
                    new ObservedPathEntryExpandedPathComparer(OldModel.PathTable.ExpandedPathComparer)),
                computation.PathSet.ObservedAccessedFileNames,
                computation.PathSet.UnsafeOptions);

            return computation.UnsafeOverride(
                pathSet,
                Convert(computation.ObservedInputs, this, (i, me) => me.Convert(i)));
        }

        private ObservedPathEntry Convert(ObservedPathEntry observedInput)
        {
            if (AreGraphsSame)
            {
                return observedInput;
            }

            return new ObservedPathEntry(
                Convert(observedInput.Path),
                observedInput.Flags,
                observedInput.EnumeratePatternRegex);
        }

        private ObservedInput Convert(ObservedInput observedInput)
        {
            if (AreGraphsSame)
            {
                return observedInput;
            }

            return new ObservedInput(observedInput.Type, observedInput.Hash, new ObservedPathEntry(Convert(observedInput.PathEntry.Path), observedInput.PathEntry.Flags, observedInput.PathEntry.EnumeratePatternRegex));
        }

        public PipId Convert(PipId pipId)
        {
            if (AreGraphsSame)
            {
                return pipId;
            }

            if (!pipId.IsValid)
            {
                return pipId;
            }

            return OldModel.GetPipId(NewGraph.PipTable.GetPipSemiStableHash(pipId));
        }

        private FileArtifact Convert(FileArtifact file)
        {
            if (AreGraphsSame)
            {
                return file;
            }

            return new FileArtifact(Convert(file.Path), file.RewriteCount);
        }

        private DirectoryArtifact Convert(DirectoryArtifact directory)
        {
            if (AreGraphsSame)
            {
                return directory;
            }

            // TODO: This is wrong (loses the seal id but it probably doesn't matter since this isn't used for comparison)
            return DirectoryArtifact.CreateWithZeroPartialSealId(Convert(directory.Path));
        }

        private FileArtifactWithAttributes Convert(FileArtifactWithAttributes file)
        {
            if (AreGraphsSame)
            {
                return file;
            }

            return FileArtifactWithAttributes.FromFileArtifact(Convert(file.ToFileArtifact()), file.FileExistence);
        }

        private AbsolutePath Convert(AbsolutePath path)
        {
            if (AreGraphsSame)
            {
                return path;
            }

            var result = m_pathMap.GetOrAdd(path, this, (p, m) =>
            {
                try
                {
                    return AbsolutePath.Create(m.OldModel.PathTable, p.ToString(m.ConvertedNewModel.PathTable));
                }
#pragma warning disable ERP022 // TODO: This should really handle specific errors
                catch
                {
                    return AbsolutePath.Invalid;
                }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
            });

            var convertedPath = result.Item.Value;
            return convertedPath;
        }

        #endregion
    }
}
