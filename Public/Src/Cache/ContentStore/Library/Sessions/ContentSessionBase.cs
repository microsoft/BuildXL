// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tasks;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

namespace BuildXL.Cache.ContentStore.Sessions
{
    /// <summary>
    /// Base implementation of IContentSession. This class serves multiple purposes:
    /// 1. Add common tracing behavior to all implementations.
    /// 2. Provide default implementations for convenience and bulk methods.
    /// 3. Ensure all calls get cancelled when Shutdown is called.
    ///
    /// It is possible that subclasses may be able to implement some of the methods more efficiently, which is why the
    /// methods are often marked as virtual.
    /// </summary>
    public abstract class ContentSessionBase : StartupShutdownBase, IContentSession
    {
        /// <nodoc />
        protected readonly CounterCollection<ContentSessionBaseCounters> BaseCounters;

        /// <inheritdoc />
        public string Name { get; }

        /// <nodoc />
        protected virtual bool TraceOperationStarted => false;

        /// <nodoc />
        protected virtual bool TracePinFinished => true;

        /// <nodoc />
        protected virtual bool TraceErrorsOnly => false;

        /// <nodoc />
        protected ContentSessionBase(string name, CounterTracker? counterTracker = null)
        {
            Name = name;
            BaseCounters = CounterTracker.CreateCounterCollection<ContentSessionBaseCounters>(counterTracker);
        }

        /// <inheritdoc />
        public Task<OpenStreamResult> OpenStreamAsync(
            Context context,
            ContentHash contentHash,
            CancellationToken token,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return WithOperationContext(
                context,
                token,
                operationContext => operationContext.PerformOperationAsync(
                    Tracer,
                    () =>
                    {
                        if (contentHash.IsEmptyHash())
                        {
                            // We don't return a static instance here because the caller takes ownership.
                            return Task.FromResult(new OpenStreamResult(new MemoryStream(Array.Empty<byte>()).WithLength(0)));
                        }

                        return OpenStreamCoreAsync(
                            operationContext,
                            contentHash,
                            urgencyHint,
                            BaseCounters[ContentSessionBaseCounters.OpenStreamRetries]);
                    },
                    traceOperationStarted: TraceOperationStarted,
                    traceErrorsOnly: TraceErrorsOnly,
                    counter: BaseCounters[ContentSessionBaseCounters.OpenStream],
                    extraEndMessage: result => $"Hash={contentHash.ToShortString()}"));
        }

        /// <nodoc />
        protected abstract Task<OpenStreamResult> OpenStreamCoreAsync(
            OperationContext operationContext,
            ContentHash contentHash,
            UrgencyHint urgencyHint,
            Counter retryCounter);

        /// <inheritdoc />
        public Task<PinResult> PinAsync(
            Context context,
            ContentHash contentHash,
            CancellationToken token,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return WithOperationContext(
                context,
                token,
                operationContext => operationContext.PerformOperationAsync(
                    Tracer,
                    () =>
                    {
                        if (contentHash.IsEmptyHash())
                        {
                            return PinResult.SuccessTask;
                        }

                        return PinCoreAsync(operationContext, contentHash, urgencyHint, BaseCounters[ContentSessionBaseCounters.PinRetries]);
                    },
                    traceOperationStarted: TraceOperationStarted,
                    traceOperationFinished: TracePinFinished,
                    traceErrorsOnly: TraceErrorsOnly,
                    extraEndMessage: r => $"input=[{contentHash.ToShortString()}] size=[{r.ContentSize}]",
                    counter: BaseCounters[ContentSessionBaseCounters.Pin]));
        }

        /// <nodoc />
        protected abstract Task<PinResult> PinCoreAsync(
            OperationContext operationContext,
            ContentHash contentHash,
            UrgencyHint urgencyHint,
            Counter retryCounter);

        /// <inheritdoc />
        public Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(
            Context context,
            IReadOnlyList<ContentHash> contentHashes,
            CancellationToken token,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return WithOperationContext(
                context,
                token,
                operationContext => operationContext.PerformNonResultOperationAsync(
                    Tracer,
                    () => PinCoreAsync(operationContext, contentHashes, urgencyHint, retryCounter: BaseCounters[ContentSessionBaseCounters.PinBulkRetries], fileCounter: BaseCounters[ContentSessionBaseCounters.PinBulkFileCount]),
                    extraEndMessage: results =>
                    {
                        var copies = 0;
                        var resultString = string.Join(",", results.Select(task =>
                        {
                            // Since all bulk operations are constructed with Task.FromResult, it is safe to just access the result;
                            var result = task.Result;

                            if (result.Item is DistributedPinResult distributedPinResult)
                            {
                                if (distributedPinResult.CopyLocally)
                                {
                                    copies++;
                                }
                            }

                            return $"{contentHashes[result.Index].ToShortString()}:{result.Item}";
                        }));

                        return $"Count={contentHashes.Count}, Copies={copies}, Hashes=[{resultString}]";
                    },
                    traceOperationStarted: TraceOperationStarted,
                    traceErrorsOnly: TraceErrorsOnly,
                    counter: BaseCounters[ContentSessionBaseCounters.PinBulk]));
        }

        /// <inheritdoc />
        public virtual Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(
            Context context,
            IReadOnlyList<ContentHash> contentHashes,
            PinOperationConfiguration configuration)
        {
            return WithOperationContext(
                context,
                configuration.CancellationToken,
                operationContext => operationContext.PerformNonResultOperationAsync(
                    Tracer,
                    () => PinAsync(operationContext, contentHashes, configuration.CancellationToken, configuration.UrgencyHint),
                    extraStartMessage: $"{nameof(ContentSessionBase)} subtype {GetType().FullName} does not implement its own configurable {nameof(PinAsync)}. Falling back on {nameof(ContentSessionBase)}.{nameof(ContentSessionBase.PinAsync)}"));
        }

        /// <nodoc />
        protected virtual async Task<IEnumerable<Task<Indexed<PinResult>>>> PinCoreAsync(
            OperationContext operationContext,
            IReadOnlyList<ContentHash> contentHashes,
            UrgencyHint urgencyHint,
            Counter retryCounter,
            Counter fileCounter)
        {
            var tasks = contentHashes.Select(
                (contentHash, index) =>
                {
                    if (contentHash.IsEmptyHash())
                    {
                        return PinResult.SuccessTask.WithIndexAsync(index);
                    }

                    return PinCoreAsync(
                        operationContext,
                        contentHash,
                        urgencyHint,
                        retryCounter).WithIndexAsync(index);
                }).ToList(); // It is important to materialize a LINQ query in order to avoid calling 'PinCoreAsync' on every iteration.

            await TaskUtilities.SafeWhenAll(tasks);
            return tasks;
        }

        /// <inheritdoc />
        public Task<PlaceFileResult> PlaceFileAsync(
            Context context,
            ContentHash contentHash,
            AbsolutePath path,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            CancellationToken token,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return WithOperationContext(
                context,
                token,
                operationContext => operationContext.PerformOperationAsync(
                    Tracer,
                    () =>
                    {
                        Contract.Requires(realizationMode != FileRealizationMode.Move, "It is not allowed to move files out of the cache");
                        var fileSystem = PassThroughFileSystem.Default;

                        if (replacementMode is FileReplacementMode.SkipIfExists or FileReplacementMode.FailIfExists && fileSystem.FileExists(path))
                        {
                            return Task.FromResult(new PlaceFileResult(PlaceFileResult.ResultCode.NotPlacedAlreadyExists));
                        }

                        if (contentHash.IsEmptyHash())
                        {
                            return PlaceEmptyFileAsync(path, realizationMode, fileSystem);
                        }

                        return PlaceFileCoreAsync(
                            operationContext,
                            contentHash,
                            path,
                            accessMode,
                            replacementMode,
                            realizationMode,
                            urgencyHint,
                            BaseCounters[ContentSessionBaseCounters.PlaceFileRetries]);
                    },
                    extraStartMessage: $"({contentHash.ToShortString()},{path},{accessMode},{replacementMode},{realizationMode})",
                    traceOperationStarted: TraceOperationStarted,
                    extraEndMessage: result =>
                                     {
                                         var message = $"({contentHash.ToShortString()},{path},{accessMode},{replacementMode},{realizationMode})";
                                         if (result.Metadata == null)
                                         {
                                             return message;
                                         }

                                         return message + $" Gate.OccupiedCount={result.Metadata.GateOccupiedCount} Gate.Wait={result.Metadata.GateWaitTime.TotalMilliseconds}ms";
                                     },
                    traceErrorsOnly: TraceErrorsOnlyForPlaceFile(path),
                    counter: BaseCounters[ContentSessionBaseCounters.PlaceFile]));
        }

        private static Task<PlaceFileResult> PlaceEmptyFileAsync(AbsolutePath path, FileRealizationMode realizationMode, PassThroughFileSystem fileSystem)
        {
            var code = realizationMode switch
            {
                FileRealizationMode.None => PlaceFileResult.ResultCode.PlacedWithCopy,
                FileRealizationMode.Any => PlaceFileResult.ResultCode.PlacedWithCopy,
                FileRealizationMode.Copy => PlaceFileResult.ResultCode.PlacedWithCopy,
                FileRealizationMode.HardLink => PlaceFileResult.ResultCode.PlacedWithHardLink,
                FileRealizationMode.CopyNoVerify => PlaceFileResult.ResultCode.PlacedWithCopy,
                _ => throw new ArgumentOutOfRangeException(nameof(realizationMode), realizationMode, null)
            };

            fileSystem.CreateEmptyFile(path);
            return Task.FromResult(new PlaceFileResult(code, fileSize: 0, source: PlaceFileResult.Source.LocalCache));
        }

        /// <summary>
        /// Gets whether only errors should be traced for place file operations to the given path.
        /// </summary>
        protected virtual bool TraceErrorsOnlyForPlaceFile(AbsolutePath path) => TraceErrorsOnly;

        /// <nodoc />
        protected abstract Task<PlaceFileResult> PlaceFileCoreAsync(
            OperationContext operationContext,
            ContentHash contentHash,
            AbsolutePath path,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            UrgencyHint urgencyHint,
            Counter retryCounter);

        /// <inheritdoc />
        public Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> PlaceFileAsync(
            Context context,
            IReadOnlyList<ContentHashWithPath> hashesWithPaths,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            CancellationToken token,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return WithOperationContext(
                context,
                token,
                operationContext => operationContext.PerformNonResultOperationAsync(
                    Tracer,
                    () => PlaceFileCoreAsync(operationContext, hashesWithPaths, accessMode, replacementMode, realizationMode, urgencyHint, BaseCounters[ContentSessionBaseCounters.PlaceFileBulkRetries]),
                    traceOperationStarted: TraceOperationStarted,
                    traceOperationFinished: false,
                    traceErrorsOnly: TraceErrorsOnly,
                    counter: BaseCounters[ContentSessionBaseCounters.PlaceFileBulk]));
        }

        /// <nodoc />
        protected virtual async Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> PlaceFileCoreAsync(
            OperationContext operationContext,
            IReadOnlyList<ContentHashWithPath> hashesWithPaths,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            var tasks = hashesWithPaths.Select(
                (contentHashWithPath, index) =>
                {
                    var (contentHash, path) = contentHashWithPath;

                    Contract.Requires(realizationMode != FileRealizationMode.Move, "It is not allowed to move files out of the cache");
                    var fileSystem = PassThroughFileSystem.Default;

                    if (replacementMode is FileReplacementMode.SkipIfExists or FileReplacementMode.FailIfExists && fileSystem.FileExists(path))
                    {
                        return Task.FromResult(new PlaceFileResult(PlaceFileResult.ResultCode.NotPlacedAlreadyExists)).WithIndexAsync(index);
                    }

                    if (contentHash.IsEmptyHash())
                    {
                        return PlaceEmptyFileAsync(path, realizationMode, fileSystem).WithIndexAsync(index);
                    }

                    return PlaceFileCoreAsync(
                        operationContext,
                        contentHashWithPath.Hash,
                        contentHashWithPath.Path,
                        accessMode,
                        replacementMode,
                        realizationMode,
                        urgencyHint,
                        retryCounter).WithIndexAsync(index);
                }).ToList(); // It is important to materialize a LINQ query in order to avoid calling 'PlaceFileCoreAsync' on every iteration.

            await TaskUtilities.SafeWhenAll(tasks);
            return tasks;

        }

        /// <inheritdoc />
        Task<PutResult> IContentSession.PutFileAsync(
            Context context,
            HashType hashType,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            CancellationToken token,
            UrgencyHint urgencyHint)
        {
            return WithOperationContext(
                context,
                token,
                operationContext => operationContext.PerformOperationAsync(
                    Tracer,
                    () => PutFileCoreAsync(operationContext, hashType, path, realizationMode, urgencyHint, BaseCounters[ContentSessionBaseCounters.PutFileRetries]),
                    extraStartMessage: $"({hashType},{path},{realizationMode}) trusted=false",
                    traceOperationStarted: TraceOperationStarted,
                    extraEndMessage: result =>
                    {
                        var message = $"({hashType},{path},{realizationMode}) trusted=false";
                        if (result.MetaData == null)
                        {
                            return message;
                        }

                        return message + $" Gate.OccupiedCount={result.MetaData.GateOccupiedCount} Gate.Wait={result.MetaData.GateWaitTime.TotalMilliseconds}ms";
                    },
                    traceErrorsOnly: TraceErrorsOnly,
                    counter: BaseCounters[ContentSessionBaseCounters.PutFile]));
        }

        /// <summary>
        /// Core implementation of PutFileAsync.
        /// </summary>
        protected abstract Task<PutResult> PutFileCoreAsync(
            OperationContext operationContext,
            HashType hashType,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            UrgencyHint urgencyHint,
            Counter retryCounter);

        /// <inheritdoc />
        Task<PutResult> IContentSession.PutFileAsync(
            Context context,
            ContentHash contentHash,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            CancellationToken token,
            UrgencyHint urgencyHint)
        {
            return WithOperationContext(
                context,
                token,
                operationContext => operationContext.PerformOperationAsync(
                    Tracer,
                    () =>
                    {
                        if (contentHash.IsEmptyHash())
                        {
                            return Task.FromResult(new PutResult(contentHash, contentSize: 0, contentAlreadyExistsInCache: true));
                        }

                        return PutFileCoreAsync(
                            operationContext,
                            contentHash,
                            path,
                            realizationMode,
                            urgencyHint,
                            BaseCounters[ContentSessionBaseCounters.PutFileRetries]);
                    },
                    extraStartMessage: $"({contentHash.ToShortString()},{path},{realizationMode}) trusted=false",
                    traceOperationStarted: TraceOperationStarted,
                    extraEndMessage: result =>
                                     {
                                         var message = $"({contentHash.ToShortString()},{path},{realizationMode}) trusted=false";
                                         if (result.MetaData == null)
                                         {
                                             return message;
                                         }

                                         return message + $" Gate.OccupiedCount={result.MetaData.GateOccupiedCount} Gate.Wait={result.MetaData.GateWaitTime.TotalMilliseconds}ms";
                                     },
                    traceErrorsOnly: TraceErrorsOnly,
                    counter: BaseCounters[ContentSessionBaseCounters.PutFile]));
        }

        /// <summary>
        /// Core implementation of PutFileAsync.
        /// </summary>
        protected virtual async Task<PutResult> PutFileCoreAsync(
            OperationContext operationContext,
            ContentHash contentHash,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            var result = await PutFileCoreAsync(operationContext, contentHash.HashType, path, realizationMode, urgencyHint, retryCounter);
            if (result.Succeeded && result.ContentHash != contentHash)
            {
                return new PutResult(contentHash, $"Calculated hash={result.ContentHash} does not match caller's hash={contentHash}");
            }

            return result;
        } 

        /// <inheritdoc />
        Task<PutResult> IContentSession.PutStreamAsync(
            Context context,
            HashType hashType,
            Stream stream,
            CancellationToken token,
            UrgencyHint urgencyHint)
        {
            return WithOperationContext(
                context,
                token,
                operationContext => operationContext.PerformOperationAsync(
                    Tracer,
                    () =>
                    {
                        if (stream.CanSeek && stream.Length == 0)
                        {
                            return Task.FromResult(new PutResult(HashInfoLookup.Find(hashType).EmptyHash, contentSize: 0, contentAlreadyExistsInCache: true));
                        }

                        return PutStreamCoreAsync(operationContext, hashType, stream, urgencyHint, BaseCounters[ContentSessionBaseCounters.PutStreamRetries]);
                    },
                    extraStartMessage: $"({hashType})",
                    traceOperationStarted: TraceOperationStarted,
                    traceErrorsOnly: TraceErrorsOnly,
                    counter: BaseCounters[ContentSessionBaseCounters.PutStream]));
        }

        /// <summary>
        /// Core implementation of PutStreamAsync.
        /// </summary>
        protected abstract Task<PutResult> PutStreamCoreAsync(
            OperationContext operationContext,
            HashType hashType,
            Stream stream,
            UrgencyHint urgencyHint,
            Counter retryCounter);

        /// <inheritdoc />
        Task<PutResult> IContentSession.PutStreamAsync(
            Context context,
            ContentHash contentHash,
            Stream stream,
            CancellationToken token,
            UrgencyHint urgencyHint)
        {
            return WithOperationContext(
                context,
                token,
                operationContext => operationContext.PerformOperationAsync(
                    Tracer,
                    () =>
                    {
                        if (contentHash.IsEmptyHash())
                        {
                            return Task.FromResult(new PutResult(contentHash, contentSize: 0, contentAlreadyExistsInCache: true));
                        }

                        return PutStreamCoreAsync(
                            operationContext,
                            contentHash,
                            stream,
                            urgencyHint,
                            BaseCounters[ContentSessionBaseCounters.PutStreamRetries]);
                    },
                    extraStartMessage: $"({contentHash.ToShortString()})",
                    traceOperationStarted: TraceOperationStarted,
                    traceErrorsOnly: TraceErrorsOnly,
                    counter: BaseCounters[ContentSessionBaseCounters.PutStream]));
        }

        /// <summary>
        /// Core implementation of PutStreamAsync.
        /// </summary>
        protected virtual async Task<PutResult> PutStreamCoreAsync(
            OperationContext operationContext,
            ContentHash contentHash,
            Stream stream,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            var result = await PutStreamCoreAsync(operationContext, contentHash.HashType, stream, urgencyHint, retryCounter);
            if (result.Succeeded && result.ContentHash != contentHash)
            {
                return new PutResult(contentHash, $"Calculated hash={result.ContentHash} does not match caller's hash={contentHash}");
            }

            return result;
        }

        /// <nodoc />
        protected internal virtual CounterSet GetCounters() => BaseCounters.ToCounterSet();
    }
}
