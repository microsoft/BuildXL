// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Ipc.Interfaces;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Ipc.Common
{
    /// <summary>
    /// A low-level abstraction of the result of an <see cref="IIpcOperation"/>.
    ///
    /// Consists of an exit code <see cref="ExitCode"/> and a payload (<see cref="Payload"/>).
    /// </summary>
    public sealed class IpcResult : IIpcResult
    {
        private readonly IpcResultStatus m_exitCode;
        private readonly string m_payload;

        /// <summary>
        /// Whether the call succeeded.
        /// </summary>
        [Pure]
        public bool Succeeded => m_exitCode == IpcResultStatus.Success;

        /// <summary>
        /// Whether the call failed (the opposite of <see cref="Succeeded"/>).
        /// </summary>
        [Pure]
        public bool Failed => !Succeeded;

        /// <summary>
        /// Exit code.
        /// </summary>
        [Pure]
        public IpcResultStatus ExitCode => m_exitCode;

        /// <summary>
        /// Optional payload.
        /// </summary>
        [Pure]
        public string Payload => m_payload;

        /// <nodoc />
        public IpcResultTimestamp Timestamp { get; }

        /// <inheritdoc/>
        public TimeSpan ActionDuration { get; set; }

        /// <summary>
        /// Creates a result representing a successful IPC operation.
        /// </summary>
        public static IIpcResult Success(string payload = null) => new IpcResult(IpcResultStatus.Success, payload);

        /// <nodoc />
        public IpcResult(IpcResultStatus status, string payload) : this(status, payload, TimeSpan.Zero)
        {
        }

        /// <nodoc />
        public IpcResult(IpcResultStatus status, string payload, TimeSpan actionDuraion)
        {
            m_exitCode = status;
            m_payload = payload ?? string.Empty;
            Timestamp = new IpcResultTimestamp();
            ActionDuration = actionDuraion;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return I($"{{succeeded: {Succeeded}, payload: '{Payload}', ActionDuration: {ActionDuration}}}");
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
        /// Asynchronously serializes given <see cref="IIpcResult"/> to a stream writer.
        /// </summary>
        /// <remarks>
        /// Doesn't handle any exceptions.
        /// </remarks>
        public static async Task SerializeAsync(Stream stream, IIpcResult result, CancellationToken token)
        {
            await Utils.WriteByteAsync(stream, (byte)result.ExitCode, token);
            await Utils.WriteStringAsync(stream, result.Payload, token);
            await Utils.WriteLongAsync(stream, result.ActionDuration.Ticks, token);
            await stream.FlushAsync(token);
        }

        /// <summary>
        /// Asynchronously deserializes on object of this type from a stream reader.
        /// </summary>
        /// <remarks>
        /// Doesn't handle any exceptions.
        /// </remarks>
        public static async Task<IIpcResult> DeserializeAsync(Stream stream, CancellationToken token)
        {
            byte statusByte = await Utils.ReadByteAsync(stream, token);
            Utils.CheckSerializationFormat(Enum.IsDefined(typeof(IpcResultStatus), statusByte), "unknown IpcResult.Status byte: {0}", statusByte);
            string payload = await Utils.ReadStringAsync(stream, token);
            long actionDuration = await Utils.ReadLongAsync(stream, token);

            return new IpcResult((IpcResultStatus)statusByte, payload, TimeSpan.FromTicks(actionDuration));
        }

        /// <summary>
        /// The <see cref="Succeeded"/> property of the result is a conjunction of the
        /// corresponding properties of the arguments, and the <see cref="Payload"/>
        /// property of the result is a semicolon-separated concatenation of the corresponding
        /// properties of the arguments.
        /// </summary>
        public static IIpcResult Merge(IIpcResult lhs, IIpcResult rhs)
        {
            var mergedStatus =
                lhs.Succeeded && rhs.Succeeded ? IpcResultStatus.Success :
                lhs.Succeeded && !rhs.Succeeded ? rhs.ExitCode :
                !lhs.Succeeded && rhs.Succeeded ? lhs.ExitCode :
                IpcResultStatus.GenericError;
            return new IpcResult(mergedStatus, lhs.Payload + Environment.NewLine + rhs.Payload, lhs.ActionDuration + rhs.ActionDuration);
        }

        /// <summary>
        /// <see cref="Merge(IIpcResult, IIpcResult)"/>.
        /// </summary>
        public static IIpcResult Merge(IEnumerable<IIpcResult> ipcResults)
        {
            Contract.Requires(ipcResults != null);
            Contract.Requires(ipcResults.Any());

            return ipcResults.Count() == 1
                ? ipcResults.First()
                : ipcResults.Skip(1).Aggregate(ipcResults.First(), Merge);
        }
    }
}
