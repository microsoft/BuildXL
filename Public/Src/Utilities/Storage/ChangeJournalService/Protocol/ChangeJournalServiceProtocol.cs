// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.ComponentModel;
using System.Diagnostics.ContractsLight;
using BuildXL.Native.IO;
using BuildXL.Native.IO.Windows;
using BuildXL.Utilities.Tracing;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.Storage.ChangeJournalService.Protocol
{
    /// <summary>
    /// Request types supported by a change journal service.
    /// </summary>
    public enum RequestType : byte
    {
        /// <summary>
        /// Query the version of the service.
        /// </summary>
        QueryServiceVersion = 0,

        /// <summary>
        /// Reads journal records.
        /// </summary>
        ReadJournal = 1,

        /// <summary>
        /// Queries journal metadata.
        /// </summary>
        QueryJournal = 2,
    }

    /// <summary>
    /// Wrapper for a response which is either <typeparamref name="TResponse" /> (on success) or <see cref="ErrorResponse" />
    /// (on failure).
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:ShouldOverrideEquals")]
    public readonly struct MaybeResponse<TResponse>
        where TResponse : class
    {
        private readonly object m_value;

        /// <summary>
        /// Creates a 'maybe' wrapper representing success.
        /// </summary>
        public MaybeResponse(TResponse successResponse)
        {
            Contract.Requires(successResponse != null);
            m_value = successResponse;
        }

        /// <summary>
        /// Creates a 'maybe' wrapper representing failure.
        /// </summary>
        public MaybeResponse(ErrorResponse errorResponse)
        {
            Contract.Requires(errorResponse != null);
            m_value = errorResponse;
        }

        /// <summary>
        /// Indicates if this is an error response. If true, <see cref="Error" /> is available.
        /// Otherwise, <see cref="Response" /> is available.
        /// </summary>
        public bool IsError => m_value is ErrorResponse;

        /// <summary>
        /// Success response if <c>IsError == false</c>.
        /// </summary>
        public TResponse Response
        {
            get
            {
                Contract.Requires(!IsError);
                return (TResponse) m_value;
            }
        }

        /// <summary>
        /// Success response if <c>IsError == true</c>.
        /// </summary>
        public ErrorResponse Error
        {
            get
            {
                Contract.Requires(IsError);
                return (ErrorResponse) m_value;
            }
        }
    }

    /// <summary>
    /// Error codes specific to using the change journal service (vs. errors that would be present if
    /// accessing the change journal directly).
    /// </summary>
    public enum ErrorStatus
    {
        /// <summary>
        /// A response was ill-formed, indicating a client bug.
        /// </summary>
        ProtocolError,

        /// <summary>
        /// A specified volume GUID path was not accessible.
        /// </summary>
        FailedToOpenVolumeHandle,
    }

    /// <summary>
    /// Service response indicating a failure condition.
    /// </summary>
    public sealed class ErrorResponse
    {
        /// <summary>
        /// Error class
        /// </summary>
        public ErrorStatus Status { get; }

        /// <summary>
        /// Additional message for diagnostics.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Creates an error response.
        /// </summary>
        public ErrorResponse(ErrorStatus status, [Localizable(false)] string message)
        {
            Contract.Requires(message != null);

            Message = message;
            Status = status;
        }
    }

    /// <summary>
    /// Service request for <see cref="RequestType.QueryJournal" />.
    /// </summary>
    public sealed class QueryJournalRequest
    {
        private readonly VolumeGuidPath m_volumeGuidPath;

        /// <nodoc />
        public QueryJournalRequest(VolumeGuidPath volumeGuidPath)
        {
            Contract.Requires(volumeGuidPath.IsValid);

            m_volumeGuidPath = volumeGuidPath;
        }

        /// <summary>
        /// Volume GUID path for the volume from which to query journal state.
        /// </summary>
        [ContractVerification(false)] // TODO: cccheck bug?
        public VolumeGuidPath VolumeGuidPath
        {
            get
            {
                Contract.Ensures(Contract.Result<VolumeGuidPath>().IsValid);
                return m_volumeGuidPath;
            }
        }

        /// <nodoc />
        public static QueryJournalRequest Read(ChangeJournalServiceProtocolReader reader)
        {
            Contract.Requires(reader != null);

            var response = new QueryJournalRequest(reader.ReadVolumeGuidPath());
            return response;
        }

        /// <nodoc />
        public void Write(ChangeJournalServiceProtocolWriter writer)
        {
            Contract.Requires(writer != null);

            Contract.Assert(VolumeGuidPath.IsValid);
            writer.WriteVolumeGuidPath(VolumeGuidPath);
        }
    }

    /// <summary>
    /// Service request for <see cref="RequestType.ReadJournal" />.
    /// </summary>
    public sealed class ReadJournalRequest
    {
        /// <summary>
        /// Change journal identifier
        /// </summary>
        /// <remarks>
        /// See http://msdn.microsoft.com/en-us/library/windows/desktop/aa365720(v=vs.85).aspx
        /// </remarks>
        public readonly ulong JournalId;

        /// <summary>
        /// Start cursor (or 0 for the first available record).
        /// </summary>
        public readonly Usn StartUsn;

        /// <summary>
        /// End cursor.
        /// </summary>
        public readonly Usn? EndUsn;

        private readonly VolumeGuidPath m_volumeGuidPath;

        /// <summary>
        /// Extra read count after reading the journal exceeds <see cref="EndUsn"/> if <see cref="EndUsn"/> is specified.
        /// </summary>
        public readonly int? ExtraReadCount;

        /// <summary>
        /// Time limit for reading journal.
        /// </summary>
        public readonly TimeSpan? TimeLimit;

        /// <nodoc />
        public ReadJournalRequest(
            VolumeGuidPath volumeGuidPath,
            ulong journalId,
            Usn startUsn,
            Usn? endUsn = default(Usn?),
            int? extraReadCount = default(int?),
            TimeSpan? timeLimit = default(TimeSpan?))
        {
            Contract.Requires(volumeGuidPath.IsValid);
            Contract.Requires(!extraReadCount.HasValue || extraReadCount >= 0);

            m_volumeGuidPath = volumeGuidPath;
            JournalId = journalId;
            StartUsn = startUsn;
            EndUsn = endUsn;
            ExtraReadCount = extraReadCount;
            TimeLimit = timeLimit;
        }

        /// <summary>
        /// Volume GUID path for the volume from which to read changes.
        /// </summary>
        [ContractVerification(false)] // TODO: cccheck bug?
        public VolumeGuidPath VolumeGuidPath
        {
            get
            {
                Contract.Ensures(Contract.Result<VolumeGuidPath>().IsValid);
                return m_volumeGuidPath;
            }
        }

        /// <nodoc />
        public static ReadJournalRequest Read(ChangeJournalServiceProtocolReader reader)
        {
            Contract.Requires(reader != null);

            return new ReadJournalRequest(
                reader.ReadVolumeGuidPath(),
                journalId: reader.ReadUInt64(),
                startUsn: new Usn(reader.ReadUInt64()),
                endUsn: reader.ReadBoolean() ? new Usn(reader.ReadUInt64()) : (Usn?) null,
                extraReadCount: reader.ReadBoolean() ? reader.ReadInt32() : (int?) null,
                timeLimit: reader.ReadBoolean() ? TimeSpan.FromTicks(reader.ReadInt64()) : (TimeSpan?) null);
        }

        /// <nodoc />
        public void Write(ChangeJournalServiceProtocolWriter writer)
        {
            Contract.Requires(writer != null);

            writer.WriteVolumeGuidPath(VolumeGuidPath);
            writer.Write(JournalId);
            writer.Write(StartUsn.Value);

            writer.Write(EndUsn.HasValue);

            if (EndUsn.HasValue)
            {
                writer.Write(EndUsn.Value.Value);
            }

            writer.Write(ExtraReadCount.HasValue);

            if (ExtraReadCount.HasValue)
            {
                writer.Write(ExtraReadCount.Value);
            }

            writer.Write(TimeLimit.HasValue);

            if (TimeLimit.HasValue)
            {
                writer.Write(TimeLimit.Value.Ticks);
            }
        }
    }

    /// <summary>
    /// Counters for journal reading.
    /// </summary>
    public enum ReadJournalCounter
    {
        /// <summary>
        /// The amount of time to read without the false-positive validation.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ReadRelevantJournalDuration,

        /// <summary>
        /// The amount of time to read without the false-positive validation.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        FalsePositiveValidationDuration,

        /// <summary>
        /// Number of records processed.
        /// </summary>
        [CounterType(CounterType.Numeric)]
        RecordsProcessedCount,

        /// <summary>
        /// Number of relevant records.
        /// </summary>
        [CounterType(CounterType.Numeric)]
        RecordsRelevantCount,

        /// <summary>
        /// Number of records related to link impact.
        /// </summary>
        [CounterType(CounterType.Numeric)]
        LinkImpactCount,

        /// <summary>
        /// Number of records related to membership impact.
        /// </summary>
        [CounterType(CounterType.Numeric)]
        MembershipImpactCount,

        /// <summary>
        /// The amount of time to handle relevant records due to link impacts.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        HandleRelevantChangesDueToLinkImpactTime,

        /// <summary>
        /// The amount of time to handle relevant records due to membership impacts.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        HandleRelevantChangesDueToMembershipImpactTime,

        /// <summary>
        /// Number of paths invalidated.
        /// </summary>
        [CounterType(CounterType.Numeric)]
        PathsInvalidatedCount,

        /// <summary>
        /// Number of existential changes.
        /// </summary>
        [CounterType(CounterType.Numeric)]
        ExistentialChangesCount,

        /// <summary>
        /// The amount of time to validate anti dependencies.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ValidateAntiDependencies,

        /// <summary>
        /// The amount of time to validate enumeration dependencies.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ValidateEnumerationDependencies,

        /// <summary>
        /// The amount of time to validate hard link changes.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ValidateHardlinkChanges,

        /// <summary>
        /// Number of existential changes suppressed due to false positives.
        /// </summary>
        [CounterType(CounterType.Numeric)]
        ExistentialChangesSuppressedAfterVerificationCount,

        /// <summary>
        /// The amount of time to enumerate the changed junction roots.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        EnumerateChangedJunctionRoots,

        /// <summary>
        /// Number of ignored records because the path is one of the unchanged junction roots.
        /// </summary>
        [CounterType(CounterType.Numeric)]
        IgnoredRecordsDueToUnchangedJunctionRootCount,
    }

    /// <summary>
    /// Service request for <see cref="RequestType.ReadJournal" />.
    /// </summary>
    public sealed class ReadJournalResponse
    {
        /// <summary>
        /// The next USN that should be read if trying to read more of the journal at a later time.
        /// </summary>
        public readonly Usn NextUsn;

        /// <summary>
        /// Status indication of the read attempt.
        /// </summary>
        public readonly ReadUsnJournalStatus Status;

        /// <summary>
        /// Indicates if journal scanning reached timeout.
        /// </summary>
        public readonly bool Timeout;

        /// <nodoc />
        public ReadJournalResponse(
            Usn nextUsn,
            ReadUsnJournalStatus status,
            bool timeout = false)
        {
            NextUsn = nextUsn;
            Status = status;
            Timeout = timeout;
        }

        /// <nodoc />
        public static ReadJournalResponse Read(ChangeJournalServiceProtocolReader reader)
        {
            Contract.Requires(reader != null);

            var response = new ReadJournalResponse(
                status: reader.ReadUsnJournalReadStatus(),
                nextUsn: new Usn(reader.ReadUInt64()),
                timeout: reader.ReadBoolean());

            return response;
        }

        /// <nodoc />
        public void Write(ChangeJournalServiceProtocolWriter writer)
        {
            Contract.Requires(writer != null);

            writer.WriteUsnJournalReadStatus(Status);
            writer.Write(NextUsn.Value);
            writer.Write(Timeout);
        }
    }
}
