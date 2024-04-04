// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities.Core;

#nullable enable

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
        /// If this IpcResult was created by merging other IIpcResult objects, this list contains their
        /// payloads; otherwise, it is null.
        /// </summary>
        private readonly List<(IpcResultStatus Status, string Payload)>? m_mergedPayloads;

        /// <summary>
        /// Whether the call succeeded.
        /// </summary>
        public bool Succeeded => m_exitCode == IpcResultStatus.Success;

        /// <summary>
        /// Whether the call failed (the opposite of <see cref="Succeeded"/>).
        /// </summary>
        public bool Failed => !Succeeded;

        /// <summary>
        /// Exit code.
        /// </summary>
        public IpcResultStatus ExitCode => m_exitCode;

        /// <summary>
        /// Optional payload.
        /// </summary>
        public string Payload
        {
            get
            {
                if (m_mergedPayloads == null)
                {
                    return m_payload;
                }

                // This is a merged result. Need to join payloads.
                return string.Join(Environment.NewLine, m_mergedPayloads.Select(x => x.Payload));
            }
        }

        /// <nodoc />
        public IpcResultTimestamp Timestamp { get; }

        /// <inheritdoc/>
        public TimeSpan ActionDuration { get; set; }

        /// <summary>
        /// Creates a result representing a successful IPC operation.
        /// </summary>
        public static IIpcResult Success(string? payload = null) => new IpcResult(IpcResultStatus.Success, payload);

        /// <nodoc />
        public IpcResult(IpcResultStatus status, string? payload) : this(status, payload, TimeSpan.Zero)
        {
        }

        /// <nodoc />
        public IpcResult(IpcResultStatus status, string? payload, TimeSpan actionDuraion)
        {
            m_exitCode = status;
            m_payload = payload ?? string.Empty;
            Timestamp = new IpcResultTimestamp();
            ActionDuration = actionDuraion;
            m_mergedPayloads = null;
        }

        private IpcResult(IpcResultStatus status, TimeSpan actionDuraion, List<(IpcResultStatus, string)> mergedPayloads)
        {
            m_exitCode = status;
            // Payload won't be merged until it is needed.
            m_payload = string.Empty;
            Timestamp = new IpcResultTimestamp();
            ActionDuration = actionDuraion;
            m_mergedPayloads = mergedPayloads;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            using var wrappedBuilder = Pools.GetStringBuilder();
            return ToString(wrappedBuilder.Instance).ToString();
        }

        /// <summary>
        /// Writes a string that represent the current object into a provided StringBuilder.
        /// </summary>
        public StringBuilder ToString(StringBuilder builder)
        {
            if (m_mergedPayloads == null)
            {
                return builder.Append($"{{succeeded: {Succeeded}, payload: '{Payload}', ActionDuration: {ActionDuration.ToString("c")}}}");
            }

            builder.Append($"{{succeeded: {Succeeded}, payload: '");
#if NET5_0_OR_GREATER
            builder.AppendJoin(Environment.NewLine, m_mergedPayloads.Select(x => x.Payload));
#else
            builder.Append(string.Join(Environment.NewLine, m_mergedPayloads.Select(x => x.Payload)));
#endif
            return builder.Append($"', ActionDuration: {ActionDuration.ToString("c")}}}");
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
        /// For a failed IpcResult, creates a new result with payload set to the first non-successful payload.
        /// </summary>
        /// <remarks>
        /// Throws if this object represent a successful result.
        /// </remarks>
        public IIpcResult GetFirstErrorResult()
        {
            Contract.Assert(!Succeeded);

            if (m_mergedPayloads == null)
            {
                return this;
            }

            foreach (var mergedPayload in m_mergedPayloads)
            {
                if (mergedPayload.Status != IpcResultStatus.Success)
                {
                    // Create new IpcResult with merged exit code and duration values, but use the payload from
                    // the individual result.
                    return new IpcResult(ExitCode, mergedPayload.Payload, ActionDuration);
                }
            }

            throw new InvalidOperationException($"Expected to find a payload with a non-success exit code.");
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
            // If it's a merged result, record only the final payload value; we don't need individual pieces anymore.
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
        /// property of the result is a newline-separated concatenation of the corresponding
        /// properties of the arguments.
        /// </summary>
        public static IIpcResult Merge(IIpcResult lhs, IIpcResult rhs)
        {
            var mergedPayloads = new List<(IpcResultStatus, string)>(2);
            AddPayloadComponents(lhs, mergedPayloads);
            AddPayloadComponents(rhs, mergedPayloads);

            return new IpcResult(
                MergeStatuses(lhs.ExitCode, rhs.ExitCode),
                lhs.ActionDuration + rhs.ActionDuration,
                mergedPayloads);
        }

        /// <summary>
        /// <see cref="Merge(IIpcResult, IIpcResult)"/>.
        /// </summary>
        public static IIpcResult Merge(IEnumerable<IIpcResult> ipcResults)
        {
            Contract.Requires(ipcResults != null);
            Contract.Requires(ipcResults.Any());

            var status = IpcResultStatus.Success;
            var duration = TimeSpan.FromSeconds(0);
            var mergedPayloads = new List<(IpcResultStatus, string)>();
            foreach (var ipcResult in ipcResults)
            {
                status = MergeStatuses(status, ipcResult.ExitCode);
                duration += ipcResult.ActionDuration;
                AddPayloadComponents(ipcResult, mergedPayloads);
            }

            return new IpcResult(status, duration, mergedPayloads);
        }

        private static IpcResultStatus MergeStatuses(IpcResultStatus lhs, IpcResultStatus rhs)
        {
            return
                success(lhs)  && success(rhs)  ? IpcResultStatus.Success :
                success(lhs)  && !success(rhs) ? rhs :
                !success(lhs) && success(rhs)  ? lhs :
                // If both sides have the same error code, preserve it. Otherwise fold everything into a GenericError
                lhs == rhs                     ? lhs :
                IpcResultStatus.GenericError;

            bool success(IpcResultStatus s) => s == IpcResultStatus.Success;
        }

        private static void AddPayloadComponents(IIpcResult result, List<(IpcResultStatus, string)> mergedPayloads)
        {
            if (result is IpcResult ipcResult && ipcResult.m_mergedPayloads != null)
            {
                // This is a merged result. Use its payload pieces instead of triggering payload computation.
                mergedPayloads.AddRange(ipcResult.m_mergedPayloads);
            }
            else
            {
                mergedPayloads.Add((result.ExitCode, result.Payload));
            }
        }
    }
}
