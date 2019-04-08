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
    /// Response class used for identifying reponses from a server to concrete requests issued from a single connection stream.
    ///
    /// Wraps an <see cref="Result"/> and a request id (<see cref="Request.Id"/>).
    /// </summary>
    public sealed class Response
    {
        /// <summary>Notification that server sends to clients when it's shutting down.</summary>
        public static readonly Response DisconnectResponse = new Response(-1, new IpcResult(status: IpcResultStatus.Success, payload: "<<DISCONNECT>>"));

        /// <nodoc/>
        public int RequestId { get; }

        /// <nodoc/>
        public IIpcResult Result { get; }

        /// <nodoc/>
        public bool IsDisconnectResponse => RequestId == DisconnectResponse.RequestId;

        /// <nodoc/>
        public Response(int requestId, IIpcResult result)
        {
            Contract.Requires(result != null);

            RequestId = requestId;
            Result = result;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return I($"{{id: {RequestId}, result: {Result}}}");
        }

        /// <summary>
        /// Serializes this object to a stream writer.
        /// </summary>
        /// <remarks>
        /// Doesn't handle any exceptions.
        /// </remarks>
        public async Task SerializeAsync(Stream stream, CancellationToken token)
        {
            await Utils.WriteIntAsync(stream, RequestId, token);
            await IpcResult.SerializeAsync(stream, Result, token);
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
        /// Deserializes on object of this type from a binary reader.
        /// </summary>
        /// <remarks>
        /// Doesn't handle any exceptions.
        /// </remarks>
        public static async Task<Response> DeserializeAsync(Stream stream, CancellationToken token)
        {
            var id = await Utils.ReadIntAsync(stream, token);
            var result = await IpcResult.DeserializeAsync(stream, token);
            return new Response(id, result);
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
        public static Task<Response> DeserializeAsync(Stream stream) => DeserializeAsync(stream, CancellationToken.None);
    }
}
