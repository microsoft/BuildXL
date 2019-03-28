// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Text;
using BuildXL.Utilities;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.Storage.ChangeJournalService.Protocol
{
    /// <summary>
    /// Stream reader for change journal service protocol primitives.
    /// </summary>
    public sealed class ChangeJournalServiceProtocolReader : BinaryReader
    {
        /// <summary>
        /// Wraps a reader over an existing stream.
        /// </summary>
        public ChangeJournalServiceProtocolReader(Stream stream, bool leaveOpen = false)
            : base(stream, Encoding.UTF8, leaveOpen)
        {
        }

        /// <summary>
        /// Reads a <see cref="RequestType" />, throwing if it is unknown.
        /// </summary>
        public RequestType ReadRequestFrame()
        {
            byte requestTypeByte = ReadByte();

            switch (requestTypeByte)
            {
                case (byte) RequestType.QueryServiceVersion:
                    return RequestType.QueryServiceVersion;
                case (byte) RequestType.QueryJournal:
                    return RequestType.QueryJournal;
                case (byte) RequestType.ReadJournal:
                    return RequestType.ReadJournal;
                default:
                    throw new BuildXLException("Unknown request type");
            }
        }

        /// <summary>
        /// Reads a response header ('true' indicates a success response).
        /// </summary>
        public bool ReadResponseHeader()
        {
            return ReadBoolean();
        }

        /// <summary>
        /// Reads an <see cref="ErrorStatus" /> introducing an error response, throwing if the status is unknown.
        /// </summary>
        public ErrorStatus ReadErrorStatus()
        {
            byte errorByte = ReadByte();

            switch (errorByte)
            {
                case (byte) ErrorStatus.ProtocolError:
                    return ErrorStatus.ProtocolError;
                case (byte) ErrorStatus.FailedToOpenVolumeHandle:
                    return ErrorStatus.FailedToOpenVolumeHandle;
                default:
                    throw new BuildXLException("Unknown error status");
            }
        }

        /// <summary>
        /// Reads either the given response type or a generic error response, based on a preceding response header (see
        /// <see cref="ReadResponseHeader" />).
        /// </summary>
        public MaybeResponse<TResponse> ReadResponseOrError<TResponse>(Func<ChangeJournalServiceProtocolReader, TResponse> readResponse)
            where TResponse : class
        {
            Contract.Requires(readResponse != null);

            if (ReadResponseHeader())
            {
                TResponse response = readResponse(this);
                Contract.Assume(response != null);
                return new MaybeResponse<TResponse>(response);
            }

            ErrorStatus status = ReadErrorStatus();
            string message = ReadString();
            return new MaybeResponse<TResponse>(new ErrorResponse(status, message));
        }
    }

    /// <summary>
    /// Stream reader for change journal service protocol primitives.
    /// </summary>
    public sealed class ChangeJournalServiceProtocolWriter : BinaryWriter
    {
        /// <summary>
        /// Wraps a writer over an existing stream.
        /// </summary>
        public ChangeJournalServiceProtocolWriter(Stream stream, bool leaveOpen = false)
            : base(stream, Encoding.UTF8, leaveOpen)
        {
        }

        /// <summary>
        /// Writes a <see cref="RequestType" />.
        /// </summary>
        public void WriteRequestFrame(RequestType requestType)
        {
            Write((byte) requestType);
        }

        /// <summary>
        /// Writes a response header, indicating if a normal response (true) or error resposne (false) follows.
        /// </summary>
        public void WriteResponseHeader(bool isSuccess = true)
        {
            Write(isSuccess);
        }

        /// <summary>
        /// Writes an <see cref="ErrorStatus" />.
        /// </summary>
        public void WriteErrorStatus(ErrorStatus status)
        {
            Write((byte) status);
        }

        /// <summary>
        /// Writes an error response, preceded by an error-level response header.
        /// </summary>
        public void WriteResponseHeaderAndError(ErrorResponse error)
        {
            Contract.Requires(error != null);
            Contract.Requires(error.Message != null);

            WriteResponseHeader(isSuccess: false);
            WriteErrorStatus(error.Status);
            Write(error.Message);
        }

        /// <summary>
        /// Writes either a success or error response by unwrapping the <paramref name="maybeResponse" />.
        /// The response is preceded by a header indicating whether or not an error response follows.
        /// </summary>
        public void WriteResponseOrError<TResponse>(
            MaybeResponse<TResponse> maybeResponse,
            Action<ChangeJournalServiceProtocolWriter, TResponse> writeResponse)
            where TResponse : class
        {
            Contract.Requires(writeResponse != null);

            if (!maybeResponse.IsError)
            {
                WriteResponseHeader(isSuccess: true);
                writeResponse(this, maybeResponse.Response);
            }
            else
            {
                WriteResponseHeaderAndError(maybeResponse.Error);
            }
        }
    }
}
