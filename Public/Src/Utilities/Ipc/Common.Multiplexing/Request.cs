// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Ipc.Interfaces;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Ipc.Common.Multiplexing
{
    /// <summary>
    /// Request class used for identifying multiple requests to be issued over the same connection stream.
    ///
    /// Wraps an <see cref="Operation"/> and adds a unique ID to it.
    ///
    /// Uses a static counter incremented by the public constructor to assign unique IDs to all created instances.
    ///
    /// Immutable.
    /// </summary>
    public sealed class Request
    {
        /// <summary>Request to stop processing further requests from the current connection.</summary>
        public static readonly Request StopRequest = new Request(-1, new IpcOperation(payload: "<<STOP>>", waitForServerAck: false));

        private static int s_requestIdCounter = 0;

        /// <nodoc/>
        public int Id { get; }

        /// <nodoc/>
        public IIpcOperation Operation { get; }

        /// <nodoc/>
        public bool IsStopRequest => Id == StopRequest.Id;

        /// <nodoc/>
        public Request(IIpcOperation operation)
        {
            Contract.Requires(operation != null);

            Id = Interlocked.Increment(ref s_requestIdCounter);
            Operation = operation;
        }

        private Request(int id, IIpcOperation operation)
        {
            Contract.Requires(operation != null);

            Id = id;
            Operation = operation;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return I($"{{id: {Id}, operation: {Operation}}}");
        }

        /// <summary>
        /// Serializes this object to a stream writer.
        /// </summary>
        /// <remarks>
        /// Doesn't handle any exceptions.
        /// </remarks>
        public async Task SerializeAsync(Stream stream, CancellationToken token)
        {
            await Utils.WriteIntAsync(stream, Id, token);
            await IpcOperation.SerializeAsync(stream, Operation, token);
            await stream.FlushAsync(token);
        }

        /// <summary>
        /// Attempts to serialize this object to a stream writer (<see cref="SerializeAsync(Stream, CancellationToken)"/>).
        /// Ignores all exceptions; just returns a boolean indication as to whether an exception happened or not.
        /// </summary>
        public async Task<bool> TrySerializeAsync(Stream stream, CancellationToken token)
        {
            try
            {
                await SerializeAsync(stream, token);
                return true;
            }
#pragma warning disable ERP022 // TODO: This should really handle specific errors
            catch
            {
                return false;
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
        }

        /// <summary>
        /// Deserializes on object of this type from a stream reader.
        /// </summary>
        /// <remarks>
        /// Doesn't handle any exceptions.
        /// </remarks>
        public static async Task<Request> DeserializeAsync(Stream reader, CancellationToken token)
        {
            var id = await Utils.ReadIntAsync(reader, token);
            var operation = await IpcOperation.DeserializeAsync(reader, token);
            var request = new Request(id, operation);
            return request;
        }

        /// <summary>
        /// Non-cancellable version of <see cref="SerializeAsync(Stream, CancellationToken)"/>.
        /// </summary>
        public Task SerializeAsync(Stream stream) => SerializeAsync(stream, CancellationToken.None);

        /// <summary>
        /// Non-cancellable version of <see cref="TrySerializeAsync(Stream, CancellationToken)"/>.
        /// </summary>
        public Task<bool> TrySerializeAsync(Stream stream) => TrySerializeAsync(stream, CancellationToken.None);

        /// <summary>
        /// Non-cancellable version of <see cref="DeserializeAsync(Stream, CancellationToken)"/>.
        /// </summary>
        public static Task<Request> DeserializeAsync(Stream stream) => DeserializeAsync(stream, CancellationToken.None);
    }
}
