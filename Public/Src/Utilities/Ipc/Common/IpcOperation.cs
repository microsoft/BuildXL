// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Ipc.Interfaces;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Ipc.Common
{
    /// <summary>
    /// A low-level abstraction for an IPC operation.
    ///
    /// Consists of a payload represented as a string (<see cref="Payload"/>), and a
    /// marker indicating whether this operation should be executed synchronously or
    /// asynchronously (<see cref="ShouldWaitForServerAck"/>).
    /// </summary>
    public sealed class IpcOperation : IIpcOperation
    {
        /// <summary>
        /// Whether this is a synchronous operation.
        /// </summary>
        [Pure]
        public bool ShouldWaitForServerAck { get; }

        /// <summary>
        /// Payload of the operation, to be transmitted to the other end as is.
        /// </summary>
        [Pure]
        public string Payload { get; }

        /// <nodoc />
        public IpcOperationTimestamp Timestamp { get; }

        /// <nodoc />
        public IpcOperation(string payload, bool waitForServerAck)
        {
            Payload = payload;
            ShouldWaitForServerAck = waitForServerAck;
            Timestamp = new IpcOperationTimestamp();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return I($"{{waitForAck: {ShouldWaitForServerAck}, payload: '{Payload}'}}");
        }

        /// <summary>
        /// Asynchronously serializes this object to a stream writer.
        /// </summary>
        /// <remarks>
        /// Doesn't handle any exceptions.
        /// </remarks>
        public Task SerializeAsync(Stream stream, CancellationToken token)
        {
            return SerializeAsync(stream, this, token);
        }

        /// <summary>
        /// Asynchronously serializes given <see cref="IIpcOperation"/> to a stream writer.
        /// </summary>
        /// <remarks>
        /// Doesn't handle any exceptions.
        /// </remarks>
        public static async Task SerializeAsync(Stream stream, IIpcOperation op, CancellationToken token)
        {
            await Utils.WriteBooleanAsync(stream, op.ShouldWaitForServerAck, token);
            await Utils.WriteStringAsync(stream, op.Payload, token);
            await stream.FlushAsync(token);
        }

        /// <summary>
        /// Asynchronously deserializes on object of this type from a stream reader.
        /// </summary>
        /// <remarks>
        /// Doesn't handle any exceptions.
        /// </remarks>
        public static async Task<IIpcOperation> DeserializeAsync(Stream stream, CancellationToken token)
        {
            var waitForAck = await Utils.ReadBooleanAsync(stream, token);
            var payload = await Utils.ReadStringAsync(stream, token);
            return new IpcOperation(payload, waitForAck);
        }
    }
}
