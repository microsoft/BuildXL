// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Pips
{
    /// <summary>
    /// Result of executing a pip.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public readonly struct PipResult
    {
        /// <summary>
        /// Singleton result for <see cref="PipResultStatus.Skipped"/>. This result does not carry any performance info.
        /// </summary>
        public static readonly PipResult Skipped = CreateForNonExecution(PipResultStatus.Skipped);

        /// <nodoc />
        public readonly PipResultStatus Status;

        /// <nodoc />
        public readonly bool MustBeConsideredPerpetuallyDirty;

        /// <nodoc />
        public readonly PipExecutionPerformance PerformanceInfo;

        /// <nodoc />
        public readonly ReadOnlyArray<AbsolutePath> DynamicallyObservedFiles;

        /// <nodoc />
        public readonly ReadOnlyArray<AbsolutePath> DynamicallyObservedEnumerations;

        /// <nodoc />
        public PipResult(
            PipResultStatus status,
            PipExecutionPerformance performanceInfo,
            bool mustBeConsideredPerpetuallyDirty,
            ReadOnlyArray<AbsolutePath> dynamicallyObservedFiles,
            ReadOnlyArray<AbsolutePath> dynamicallyObservedEnumerations)
        {
            Contract.Requires(!status.IndicatesExecution() == (performanceInfo == null));
            Contract.Requires(dynamicallyObservedFiles.IsValid);
            Contract.Requires(dynamicallyObservedEnumerations.IsValid);

            Status = status;
            PerformanceInfo = performanceInfo;
            MustBeConsideredPerpetuallyDirty = mustBeConsideredPerpetuallyDirty;
            DynamicallyObservedFiles = dynamicallyObservedFiles;
            DynamicallyObservedEnumerations = dynamicallyObservedEnumerations;
        }

        /// <summary>
        /// Creates a <see cref="PipResult"/> with the given status. The performance info is populated
        /// with zero duration (start / stop right now) and no dynamic observed files or enumerations
        /// </summary>
        public static PipResult CreateWithPointPerformanceInfo(PipResultStatus status, bool mustBeConsideredPerpetuallyDirty = false)
        {
            Contract.Requires(status.IndicatesExecution());
            return new PipResult(
                status,
                PipExecutionPerformance.CreatePoint(status),
                mustBeConsideredPerpetuallyDirty,
                ReadOnlyArray<AbsolutePath>.Empty,
                ReadOnlyArray<AbsolutePath>.Empty);
        }

        /// <summary>
        /// Creates a <see cref="PipResult"/> with the given status. The performance info is populated
        /// as a duration from <paramref name="executionStart"/> to now without any dynamic observed files or enumerations
        /// </summary>
        public static PipResult Create(PipResultStatus status, DateTime executionStart, bool mustBeConsideredPerpetuallyDirty = false)
        {
            Contract.Requires(status.IndicatesExecution());
            Contract.Requires(executionStart.Kind == DateTimeKind.Utc);
            return new PipResult(
                status,
                PipExecutionPerformance.Create(status, executionStart),
                mustBeConsideredPerpetuallyDirty,
                ReadOnlyArray<AbsolutePath>.Empty,
                ReadOnlyArray<AbsolutePath>.Empty);
        }

        /// <summary>
        /// Creates a <see cref="PipResult"/> indicating that a pip wasn't actually executed. No performance info is attached.
        /// </summary>
        public static PipResult CreateForNonExecution(PipResultStatus status, bool mustBeConsideredPerpetuallyDirty = false)
        {
            Contract.Requires(!status.IndicatesExecution());
            return new PipResult(
                status,
                null,
                mustBeConsideredPerpetuallyDirty,
                ReadOnlyArray<AbsolutePath>.Empty,
                ReadOnlyArray<AbsolutePath>.Empty);
        }
    }

    /// <summary>
    /// Summary result of running a pip.
    /// </summary>
    public enum PipResultStatus : byte
    {
        /// <summary>
        /// The pip executed and succeeded.
        /// </summary>
        Succeeded,

        /// <summary>
        /// The pip decided that it did not need to execute or copy outputs from cache, since its outputs
        /// were already up to date.
        /// </summary>
        UpToDate,

        /// <summary>
        /// The correct output content was not already present, but was deployed from a cache rather than produced a new one.
        /// </summary>
        DeployedFromCache,

        /// <summary>
        /// The pip cannot run from cache, and it is not executed.
        /// </summary>
        NotCachedNotExecuted,

        /// <summary>
        /// The pip can run from cache, but it defers materialization of the correct outputs, i.e., no deployment from cache.
        /// </summary>
        NotMaterialized,

        /// <summary>
        /// The pip attempted to execute, but failed.
        /// </summary>
        Failed,

        /// <summary>
        /// The scheduler decides that pip had to be skipped.
        /// </summary>
        Skipped,

        /// <summary>
        /// Execution of this pip was canceled (after being ready and queued).
        /// </summary>
        Canceled,
    }

    /// <summary>
    /// Extensions for <see cref="PipResultStatus" />
    /// </summary>
    public static class PipResultStatusExtensions
    {
        /// <summary>
        /// Indicates if a pip's result indicates that it failed.
        /// </summary>
        [Pure]
        public static bool IndicatesFailure(this PipResultStatus result)
        {
            return result == PipResultStatus.Failed || result == PipResultStatus.Skipped || result == PipResultStatus.Canceled;
        }

        /// <summary>
        /// Indicates if a pip's result indications some level of execution, though possibly just an up-to-date check (i.e., not skipped entirely).
        /// </summary>
        [Pure]
        public static bool IndicatesExecution(this PipResultStatus result)
        {
            Contract.Ensures(ContractUtilities.Static(
                   !Contract.Result<bool>() ||
                   result == PipResultStatus.UpToDate || result == PipResultStatus.NotMaterialized || result == PipResultStatus.DeployedFromCache ||
                   result == PipResultStatus.Succeeded || result == PipResultStatus.Failed || result == PipResultStatus.Canceled));

            return result == PipResultStatus.UpToDate || result == PipResultStatus.NotMaterialized || result == PipResultStatus.DeployedFromCache ||
                   result == PipResultStatus.Succeeded || result == PipResultStatus.Failed || result == PipResultStatus.Canceled;
        }

        /// <summary>
        /// Converts this result to a value indicating execution vs. cache status.
        /// The result must indicate execution (see <see cref="IndicatesExecution"/>).
        /// </summary>
        [Pure]
        [ContractVerification(false)]
        public static PipExecutionLevel ToExecutionLevel(this PipResultStatus result)
        {
            Contract.Requires(result.IndicatesExecution());

            switch (result)
            {
                case PipResultStatus.Succeeded:
                    return PipExecutionLevel.Executed;
                case PipResultStatus.Failed:
                case PipResultStatus.Canceled:
                    return PipExecutionLevel.Failed;
                case PipResultStatus.DeployedFromCache:
                case PipResultStatus.NotMaterialized: // TODO: This is misleading; should account for eventual materialization.
                    return PipExecutionLevel.Cached;
                case PipResultStatus.UpToDate:
                    return PipExecutionLevel.UpToDate;
                default:
                    throw Contract.AssertFailure("Unhandled Pip Result that indicates execution");
            }
        }
    }
}
