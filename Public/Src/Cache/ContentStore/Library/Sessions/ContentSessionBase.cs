// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Sessions
{
    /// <summary>
    ///     Base implementation of IContentSession. The purpose of having this class is to add common tracing
    /// behavior to all implementations.
    ///
    ///     Note that this is intended to be subclassed by readonly content sessions but implements the full
    /// IContentSession, which is why methods for IContentSession are hidden by implementing them explicitly
    /// and making the Core implementations virtual and not abstract. The constraint that forced this design
    /// is that C# does not allow for multiple inheritance, and this was the only way to get base implementations
    /// for IContentSession.
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
        protected ContentSessionBase(string name, CounterTracker counterTracker = null)
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
                    () => OpenStreamCoreAsync(operationContext, contentHash, urgencyHint, BaseCounters[ContentSessionBaseCounters.OpenStreamRetries]),
                    traceOperationStarted: TraceOperationStarted,
                    traceErrorsOnly: TraceErrorsOnly,
                    counter: BaseCounters[ContentSessionBaseCounters.OpenStream]));
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
                    () => PinCoreAsync(operationContext, contentHash, urgencyHint, BaseCounters[ContentSessionBaseCounters.PinRetries]),
                    traceOperationStarted: TraceOperationStarted,
                    traceOperationFinished: TracePinFinished,
                    traceErrorsOnly: TraceErrorsOnly,
                    extraEndMessage: _ => $"input=[{contentHash.ToShortString()}]",
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
        public Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(
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
                    extraStartMessage: $"{nameof(ContentSessionBase)} subtype {GetType().FullName} does not implement its own {nameof(IConfigurablePin)}.{nameof(IConfigurablePin.PinAsync)}. Falling back on {nameof(ContentSessionBase)}.{nameof(ContentSessionBase.PinAsync)}"));
        }

        /// <nodoc />
        protected abstract Task<IEnumerable<Task<Indexed<PinResult>>>> PinCoreAsync(
            OperationContext operationContext,
            IReadOnlyList<ContentHash> contentHashes,
            UrgencyHint urgencyHint,
            Counter retryCounter,
            Counter fileCounter);

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
                    () => PlaceFileCoreAsync(operationContext, contentHash, path, accessMode, replacementMode, realizationMode, urgencyHint, BaseCounters[ContentSessionBaseCounters.PlaceFileRetries]),
                    extraStartMessage: $"({contentHash.ToShortString()},{path},{accessMode},{replacementMode},{realizationMode})",
                    traceOperationStarted: TraceOperationStarted,
                    extraEndMessage: result =>
                                     {
                                         var message = $"input=({contentHash.ToShortString()},{path},{accessMode},{replacementMode},{realizationMode})";
                                         if (result.Metadata == null)
                                         {
                                             return message;
                                         }

                                         return message + $" Gate.OccupiedCount={result.Metadata.GateOccupiedCount} Gate.Wait={result.Metadata.GateWaitTime.TotalMilliseconds}ms";
                                     },
                    traceErrorsOnly: TraceErrorsOnlyForPlaceFile(path),
                    counter: BaseCounters[ContentSessionBaseCounters.PlaceFile]));
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
        protected abstract Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> PlaceFileCoreAsync(
            OperationContext operationContext,
            IReadOnlyList<ContentHashWithPath> hashesWithPaths,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            UrgencyHint urgencyHint,
            Counter retryCounter);

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
                    extraStartMessage: $"({path},{realizationMode},{hashType}) trusted=false",
                    traceOperationStarted: TraceOperationStarted,
                    extraEndMessage: result =>
                    {
                        var message = $"({path},{realizationMode}) trusted=false";
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
        /// Core implementation of PutFileAsync. Made virtual so that IReadoOnlyContentSession implementations do
        /// not have to implement this.
        /// </summary>
        protected virtual Task<PutResult> PutFileCoreAsync(
            OperationContext operationContext,
            HashType hashType,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            UrgencyHint urgencyHint,
            Counter retryCounter)
            => throw new NotImplementedException();

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
                    () => PutFileCoreAsync(operationContext, contentHash, path, realizationMode, urgencyHint, BaseCounters[ContentSessionBaseCounters.PutFileRetries]),
                    extraStartMessage: $"({path},{realizationMode},{contentHash.ToShortString()}) trusted=false",
                    traceOperationStarted: TraceOperationStarted,
                    extraEndMessage: result =>
                                     {
                                         var message = $"({path},{realizationMode}) trusted=false";
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
        /// Core implementation of PutFileAsync. Made virtual so that IReadoOnlyContentSession implementations do
        /// not have to implement this.
        /// </summary>
        protected virtual Task<PutResult> PutFileCoreAsync(
            OperationContext operationContext,
            ContentHash contentHash,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            UrgencyHint urgencyHint,
            Counter retryCounter)
            => throw new NotImplementedException();

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
                    () => PutStreamCoreAsync(operationContext, hashType, stream, urgencyHint, BaseCounters[ContentSessionBaseCounters.PutStreamRetries]),
                    extraStartMessage: $"({hashType})",
                    traceOperationStarted: TraceOperationStarted,
                    traceErrorsOnly: TraceErrorsOnly,
                    counter: BaseCounters[ContentSessionBaseCounters.PutStream]));
        }

        /// <summary>
        /// Core implementation of PutStreamAsync. Made virtual so that IReadoOnlyContentSession implementations do
        /// not have to implement this.
        /// </summary>
        protected virtual Task<PutResult> PutStreamCoreAsync(
            OperationContext operationContext,
            HashType hashType,
            Stream stream,
            UrgencyHint urgencyHint,
            Counter retryCounter)
            => throw new NotImplementedException();

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
                    () => PutStreamCoreAsync(operationContext, contentHash, stream, urgencyHint, BaseCounters[ContentSessionBaseCounters.PutStreamRetries]),
                    extraStartMessage: $"({contentHash.ToShortString()})",
                    traceOperationStarted: TraceOperationStarted,
                    traceErrorsOnly: TraceErrorsOnly,
                    counter: BaseCounters[ContentSessionBaseCounters.PutStream]));
        }

        /// <summary>
        /// Core implementation of PutStreamAsync. Made virtual so that IReadoOnlyContentSession implementations do
        /// not have to implement this.
        /// </summary>
        protected virtual Task<PutResult> PutStreamCoreAsync(
            OperationContext operationContext,
            ContentHash contentHash,
            Stream stream,
            UrgencyHint urgencyHint,
            Counter retryCounter)
            => throw new NotImplementedException();

        /// <nodoc />
        protected internal virtual CounterSet GetCounters() => BaseCounters.ToCounterSet();
    }
}
