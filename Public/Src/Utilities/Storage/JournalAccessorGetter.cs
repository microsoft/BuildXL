// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Native.IO;
using BuildXL.Storage.ChangeJournalService;
using BuildXL.Storage.ChangeJournalService.Protocol;
using BuildXL.Storage.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Storage
{
    /// <summary>
    /// Getter for <see cref="IChangeJournalAccessor" />.
    /// </summary>
    public static class JournalAccessorGetter
    {
        /// <summary>
        /// Tries to get <see cref="IChangeJournalAccessor"/>.
        /// </summary>
        public static Optional<IChangeJournalAccessor> TryGetJournalAccessor(LoggingContext loggingContext, VolumeMap volumeMap, string path)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(volumeMap != null);
            Contract.Requires(!string.IsNullOrWhiteSpace(path));

            var inprocAccessor = new InProcChangeJournalAccessor();

            // If the current process is called with elevation, it can directly access the journal.
            if (CurrentProcess.IsElevated)
            {
                return inprocAccessor;
            }

            try
            {
                // If the operating system is Win10-RedStone2, it can directly access the journal as well.
                using (var file = FileUtilities.CreateFileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    var volume = volumeMap.TryGetVolumePathForHandle(file.SafeFileHandle);
                    if (volume.IsValid)
                    {
                        // Journal accessor needs to know whether the OS allows unprivileged journal operations
                        // because some operation names (e.g., reading journal, getting volume handle) are different.
                        inprocAccessor.IsJournalUnprivileged = true;

                        // Attempt to access journal. Any error means that the journal operations are not unprivileged yet in the host computer.
                        var result = inprocAccessor.QueryJournal(new QueryJournalRequest(volume));
                        if (!result.IsError && result.Response.Succeeded)
                        {
                            return inprocAccessor;
                        }
                    }
                }
            }
            catch (BuildXLException ex)
            {
                Logger.Log.FailedCheckingDirectJournalAccess(loggingContext, ex.Message);
                return default(Optional<IChangeJournalAccessor>);
            }

            return default(Optional<IChangeJournalAccessor>);
        }
    }
}
