// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
        private CounterCollection<ContentSessionBaseCounters> _counters { get; } = new CounterCollection<ContentSessionBaseCounters>();

        /// <inheritdoc />
        public string Name { get; }

        /// <nodoc />
        protected ContentSessionBase(string name)
        {
            Name = name;
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
                    () => OpenStreamCoreAsync(operationContext, contentHash, urgencyHint, _counters[ContentSessionBaseCounters.OpenStreamRetries]),
                    counter: _counters[ContentSessionBaseCounters.OpenStream]));
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
                    () => PinCoreAsync(operationContext, contentHash, urgencyHint, _counters[ContentSessionBaseCounters.PinRetries]),
                    traceOperationStarted: false,
                    extraEndMessage: _ => $"input=[{contentHash}]",
                    counter: _counters[ContentSessionBaseCounters.Pin]));
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
                    () => PinCoreAsync(operationContext, contentHashes, urgencyHint, _counters[ContentSessionBaseCounters.PinBulkRetries], _counters[ContentSessionBaseCounters.PinBulkFileCount]),
                    extraEndMessage: results =>
                    {
                        var resultString = string.Join(",", results.Select(task =>
                        {
                            // Since all bulk operations are constructed with Task.FromResult, it is safe to just access the result;
                            var result = task.Result;
                            return $"{contentHashes[result.Index]}:{result.Item}";
                        }));

                        return $"Hashes=[{resultString}]";
                    },
                    traceOperationStarted: false,
                    counter: _counters[ContentSessionBaseCounters.PinBulk]));
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
                    () => PlaceFileCoreAsync(operationContext, contentHash, path, accessMode, replacementMode, realizationMode, urgencyHint, _counters[ContentSessionBaseCounters.PlaceFileRetries]),
                    extraStartMessage: $"({contentHash},{path},{accessMode},{replacementMode},{realizationMode})",
                    extraEndMessage: (_) => $"input={contentHash}",
                    counter: _counters[ContentSessionBaseCounters.PlaceFile]));
        }

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
                    () => PlaceFileCoreAsync(operationContext, hashesWithPaths, accessMode, replacementMode, realizationMode, urgencyHint, _counters[ContentSessionBaseCounters.PlaceFileBulkRetries]),
                    traceOperationStarted: false,
                    traceOperationFinished: false,
                    counter: _counters[ContentSessionBaseCounters.PlaceFileBulk]));
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
                    () => PutFileCoreAsync(operationContext, hashType, path, realizationMode, urgencyHint, _counters[ContentSessionBaseCounters.PutFileRetries]),
                    extraStartMessage: $"({path},{realizationMode},{hashType}) trusted=false",
                    extraEndMessage: _ => "trusted=false",
                    counter: _counters[ContentSessionBaseCounters.PutFile]));
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
                    () => PutFileCoreAsync(operationContext, contentHash, path, realizationMode, urgencyHint, _counters[ContentSessionBaseCounters.PutFileRetries]),
                    extraStartMessage: $"({path},{realizationMode},{contentHash}) trusted=false",
                    extraEndMessage: _ => "trusted=false",
                    counter: _counters[ContentSessionBaseCounters.PutFile]));
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
                    () => PutStreamCoreAsync(operationContext, hashType, stream, urgencyHint, _counters[ContentSessionBaseCounters.PutStreamRetries]),
                    extraStartMessage: $"({hashType})",
                    counter: _counters[ContentSessionBaseCounters.PutStream]));
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
                    () => PutStreamCoreAsync(operationContext, contentHash, stream, urgencyHint, _counters[ContentSessionBaseCounters.PutStreamRetries]),
                    extraStartMessage: $"({contentHash})",
                    counter: _counters[ContentSessionBaseCounters.PutStream]));
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
        protected virtual CounterSet GetCounters() => _counters.ToCounterSet();
    }
}
