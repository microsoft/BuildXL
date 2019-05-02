// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Native.IO;
using BuildXL.Native.IO.Windows;
using BuildXL.Storage.ChangeJournalService.Protocol;
using BuildXL.Utilities.Tracing;
using Microsoft.Win32.SafeHandles;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Storage.ChangeJournalService
{
    /// <summary>
    /// Change journal accessor that directly operates upon a local volume's change journal.
    /// Since accessing a volume is a privileged operation, this accessor only works in an elevated process.
    /// </summary>
    public sealed class InProcChangeJournalAccessor : IChangeJournalAccessor
    {
        /// <summary>
        /// Buffer size for FSCTL_READ_USN_JOURNAL.
        /// Journal records are about 100 bytes each, so this gets about 655 records per read.
        /// </summary>
        private const int JournalReadBufferSize = 64 * 1024;

        /// <summary>
        /// Whether the journal operations are privileged or unprivileged based on the OS version
        /// </summary>
        /// <remarks>
        /// Win10-RS2 has a separate unprivileged journal operation whose ioControlCode is different.
        /// This property will decide which ioControlCode will be used to scan the journal.
        /// Also, trailing slash in the volume guid path matters for unprivileged and privileged read journal operations.
        /// </remarks>
        public bool IsJournalUnprivileged { get; set; }

        /// <inheritdoc />
        public MaybeResponse<ReadJournalResponse> ReadJournal(ReadJournalRequest request, Action<UsnRecord> onUsnRecordReceived)
        {
            Contract.Assert(request.VolumeGuidPath.IsValid);

            SafeFileHandle volumeHandle;
            OpenFileResult volumeOpenResult = OpenVolumeHandle(request.VolumeGuidPath, out volumeHandle);

            using (volumeHandle)
            {
                if (!volumeOpenResult.Succeeded)
                {
                    string message = I($"Failed to open a volume handle for the volume '{request.VolumeGuidPath.Path}'");
                    return new MaybeResponse<ReadJournalResponse>(
                        new ErrorResponse(ErrorStatus.FailedToOpenVolumeHandle, message));
                }

                Usn startUsn = request.StartUsn;
                Usn endUsn = request.EndUsn ?? Usn.Zero;
                int extraReadCount = request.ExtraReadCount ?? -1;
                long timeLimitInTicks = request.TimeLimit?.Ticks ?? -1;
                var sw = new StopwatchVar();

                using (var swRun = sw.Start())
                {
                    byte[] buffer = new byte[JournalReadBufferSize];
                    while (true)
                    {
                        if (timeLimitInTicks >= 0 && timeLimitInTicks < swRun.ElapsedTicks)
                        {
                            return new MaybeResponse<ReadJournalResponse>(
                                    new ReadJournalResponse(status: ReadUsnJournalStatus.Success, nextUsn: startUsn, timeout: true));
                        }

                        ReadUsnJournalResult result = FileUtilities.TryReadUsnJournal(
                            volumeHandle,
                            buffer,
                            request.JournalId,
                            startUsn,
                            isJournalUnprivileged: IsJournalUnprivileged);

                        if (!result.Succeeded)
                        {
                            // Bug #1164760 shows that the next USN can be non-zero.
                            return new MaybeResponse<ReadJournalResponse>(new ReadJournalResponse(status: result.Status, nextUsn: result.NextUsn));
                        }

                        if (result.Records.Count == 0)
                        {
                            return
                                new MaybeResponse<ReadJournalResponse>(
                                    new ReadJournalResponse(status: ReadUsnJournalStatus.Success, nextUsn: result.NextUsn));
                        }

                        foreach (var record in result.Records)
                        {
                            onUsnRecordReceived(record);
                        }

                        Contract.Assume(startUsn < result.NextUsn);
                        startUsn = result.NextUsn;

                        if (!endUsn.IsZero)
                        {
                            if (startUsn >= endUsn && (--extraReadCount) < 0)
                            {
                                return new MaybeResponse<ReadJournalResponse>(
                                    new ReadJournalResponse(status: ReadUsnJournalStatus.Success, nextUsn: result.NextUsn));
                            }
                        }
                    }
                }
            }
        }

        /// <inheritdoc />
        public MaybeResponse<QueryUsnJournalResult> QueryJournal(QueryJournalRequest request)
        {
            Contract.Assert(request.VolumeGuidPath.IsValid);

            SafeFileHandle volumeHandle;
            OpenFileResult volumeOpenResult = OpenVolumeHandle(request.VolumeGuidPath, out volumeHandle);

            using (volumeHandle)
            {
                if (!volumeOpenResult.Succeeded)
                {
                    string message = I($"Failed to open a volume handle for the volume '{request.VolumeGuidPath.Path}': Status: {volumeOpenResult.Status.ToString()} | Error code: {volumeOpenResult.NativeErrorCode}");
                    return new MaybeResponse<QueryUsnJournalResult>(
                            new ErrorResponse(ErrorStatus.FailedToOpenVolumeHandle, message));
                }

                QueryUsnJournalResult result = FileUtilities.TryQueryUsnJournal(volumeHandle);
                return new MaybeResponse<QueryUsnJournalResult>(result);
            }
        }

        private OpenFileResult OpenVolumeHandle(VolumeGuidPath path, out SafeFileHandle volumeHandle)
        {
            Contract.Requires(path.IsValid);

            #pragma warning disable SA1114 // Parameter list must follow declaration
            return FileUtilities.TryOpenDirectory(

                // Unprivileged and privileged read journal operations require different types of handles.
                // Do not ask why, this is learned after some painful experience :(
                IsJournalUnprivileged ? path.Path : path.GetDevicePath(),
                FileDesiredAccess.GenericRead,
                FileShare.ReadWrite | FileShare.Delete,
                FileFlagsAndAttributes.None,
                out volumeHandle);
        }
    }
}
