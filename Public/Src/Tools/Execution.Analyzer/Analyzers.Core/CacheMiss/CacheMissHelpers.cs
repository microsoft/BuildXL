// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Execution.Analyzer.Analyzers.CacheMiss
{
    internal static class CacheMissHelpers
    {
        [SuppressMessage("Microsoft.Performance", "CA1811")]
        public static bool IsHit(this ProcessFingerprintComputationEventData fingerprintComputation)
        {
            foreach (var item in fingerprintComputation.StrongFingerprintComputations)
            {
                if (item.IsStrongFingerprintHit)
                {
                    return true;
                }
            }

            return false;
        }

        public static IReadOnlyList<FileData> ToFileDataList(this IReadOnlyList<FileArtifact> files, uint workerId, AnalysisModel model)
        {
            return files.SelectList(f => new FileData()
            {
                File = f,
                Hash = model.LookupHash(workerId, f).Hash,
            });
        }

        public static IReadOnlyList<AbsolutePath> ToPathList(this IReadOnlyList<DirectoryArtifact> directories)
        {
            return directories.SelectList(d => d.Path);
        }

        public static bool TryGetUsedStrongFingerprintComputation(this ProcessFingerprintComputationEventData fingerprintComputation, out ProcessStrongFingerprintComputationData strongFingerprintComputationData)
        {
            var strongFingerprintComputations = fingerprintComputation.StrongFingerprintComputations;

            // The last computation is the computation used
            if (strongFingerprintComputations.Count > 0)
            {
                strongFingerprintComputationData = strongFingerprintComputations[strongFingerprintComputations.Count - 1];
                return true;
            }

            strongFingerprintComputationData = default(ProcessStrongFingerprintComputationData);
            return false;
        }

        public static bool TryGetComputationWithPathSet(
            this ProcessFingerprintComputationEventData fingerprintData,
            ContentHash pathSetHash,
            out ProcessStrongFingerprintComputationData match)
        {
            foreach (var computation in fingerprintData.StrongFingerprintComputations)
            {
                if (computation.PathSetHash == pathSetHash)
                {
                    match = computation;
                    return true;
                }
            }

            match = default(ProcessStrongFingerprintComputationData);
            return false;
        }

        public static bool HasPriorStrongFingerprint(
           this ProcessStrongFingerprintComputationData fingerprintData,
           StrongContentFingerprint expected)
        {
            foreach (var strongFingerprint in fingerprintData.PriorStrongFingerprints)
            {
                if (strongFingerprint == expected)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
