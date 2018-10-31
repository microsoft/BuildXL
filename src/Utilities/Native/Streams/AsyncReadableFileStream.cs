// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Native.Streams
{
    /// <summary>
    /// Async file stream which is read-only.
    /// </summary>
    public sealed class AsyncReadableFileStream : AsyncFileStream
    {
        /// <nodoc />
        internal AsyncReadableFileStream(IAsyncFile file, bool ownsFile)
            : base(file, ownsFile)
        {
            Contract.Requires(file.CanRead);
        }

        /// <inheritdoc />
        public override bool CanRead => true;

        /// <inheritdoc />
        public override bool CanWrite => false;

        /// <inheritdoc />
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            using (StreamOperationToken op = StartNewOperation(StreamOperation.Read))
            {
                BackgroundOperationSlot backgroundOperationSlot = await op.WaitForBackgroundOperationSlotAsync();

                Contract.Assume(
                    internalBuffer.State != FileBuffer.BufferState.Locked,
                    "Buffer should not be locked, since no background operation is running.");

                int bytesReadFromBuffer;
                bool fillStarted = ReadBufferAndStartFillIfEmptiedButUsable(backgroundOperationSlot, buffer, offset, count, out bytesReadFromBuffer);

                if (bytesReadFromBuffer == 0)
                {
                    if (fillStarted)
                    {
                        backgroundOperationSlot = await op.WaitForBackgroundOperationSlotAsync();
                    }

                    // We just ran a fill and waited for it (usability may be updated on its completion) or we were unable to start a fill.
                    // In either case, we should now respond to usability. We don't have any bytes read, and so we are now in some sense at the position
                    // where the unusability occurs, rather than behind it (e.g. we should quietly exhaust the buffer before complaining about EOF).
                    switch (backgroundOperationSlot.Usability)
                    {
                        case StreamUsability.Usable:
                            Contract.Assume(
                                fillStarted,
                                "ReadBufferAndStartFillIfEmptiedButUsable should have started a fill, since the stream is usable");
                            Analysis.IgnoreResult(
                                ReadBufferAndStartFillIfEmptiedButUsable(backgroundOperationSlot, buffer, offset, count, out bytesReadFromBuffer));
                            Contract.Assume(
                                bytesReadFromBuffer > 0,
                                "Usable stream implies that the completed fill obtained bytes. Zero bytes returned from ReadFile implies failure.");
                            break;
                        case StreamUsability.EndOfFileReached:
                            Contract.Assume(
                                internalBuffer.State == FileBuffer.BufferState.Empty,
                                "EndOfFileReached usability coincides with a totally-failed fill (nothing read)");
                            break;
                        case StreamUsability.Broken:
                            throw backgroundOperationSlot.ThrowExceptionForBrokenStream();
                        default:
                            throw Contract.AssertFailure("Unhandled StreamUsability");
                    }
                }

                // We've satisfied a read request for 'bytesReadFromBuffer' bytes, which advances our virtual file position.
                op.AdvancePosition(bytesReadFromBuffer);
                return bytesReadFromBuffer;
            }
        }

        /// <summary>
        /// Starts a read from the buffer. Returns a bool indicating if a fill started as a result. A fill is not started unless the stream is usable.
        /// </summary>
        private bool ReadBufferAndStartFillIfEmptiedButUsable(BackgroundOperationSlot backgroundOperationSlot, byte[] buffer, int offset, int count, out int bytesReadFromBuffer)
        {
            FileBuffer.BufferOperationStatus status = internalBuffer.Read(buffer, offset, count, out bytesReadFromBuffer);
            switch (status)
            {
                case FileBuffer.BufferOperationStatus.ReadExhausted:
                    // We start a background fill if the stream is still usable, but we don't need to explicitly handle
                    // EOF or broken-ness here:
                    // - If the caller gets bytes from the buffer, let them get used (i.e., read buffer bytes before complaining about EOF).
                    // - If the caller needs bytes right away but we don't have any, the caller should wait on the fill (if true is returned)
                    //   and check stream usability.
                    // The key point is that a dedicated caller will eventually hit the second case. We defer EOF / failure until the caller
                    // catches up to the stream position where it happens; if they do not ever read up to that point, pretend it never happened.
                    if (backgroundOperationSlot.Usability == StreamUsability.Usable)
                    {
                        backgroundOperationSlot.StartBackgroundOperation(StreamBackgroundOperation.Fill);
                        return true;
                    }
                    else
                    {
                        return false;
                    }

                case FileBuffer.BufferOperationStatus.CapacityRemaining:
                    Contract.Assume(bytesReadFromBuffer > 0);
                    return false;
                case FileBuffer.BufferOperationStatus.FlushRequired:
                    Contract.Assume(false, "Buffer is used only for reads, and so never needs to be write-flushed.");
                    throw new InvalidOperationException("Unreachable");
                default:
                    throw Contract.AssertFailure("Unhandled BufferOperationStatus");
            }
        }

        /// <inheritdoc />
        protected override Task FlushOrDiscardBufferAsync(BackgroundOperationSlot slot)
        {
            Analysis.IgnoreArgument(slot);
            internalBuffer.Discard();
            return Unit.VoidTask;
        }
    }
}
