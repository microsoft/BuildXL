// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Native.IO;
using BuildXL.Storage.ChangeJournalService;
using BuildXL.Storage.ChangeJournalService.Protocol;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

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
        public static Possible<IChangeJournalAccessor> TryGetJournalAccessor(VolumeMap volumeMap, string path)
        {
            Contract.Requires(volumeMap != null);
            Contract.Requires(!string.IsNullOrWhiteSpace(path));

            var inprocAccessor = new InProcChangeJournalAccessor();

            try
            {
                // When the current process runs with elevation, the process can access the journal directly. However, even with such a capability,
                // the process may fail in opening the volume handle. Thus, we need to verify that the process is able to open the volume handle.
                // If the operating system is Win10-RedStone2, it can directly access the journal as well.
                using (var file = FileUtilities.CreateFileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    var volume = volumeMap.TryGetVolumePathForHandle(file.SafeFileHandle);
                    if (volume.IsValid)
                    {
                        // Journal accessor needs to know whether the OS allows unprivileged journal operations
                        // because some operation names (e.g., reading journal, getting volume handle) are different.
                        inprocAccessor.IsJournalUnprivileged = !CurrentProcess.IsElevated;

                        // Attempt to access journal. Any error means that the journal operations are not unprivileged yet in the host computer.
                        var result = inprocAccessor.QueryJournal(new QueryJournalRequest(volume));

                        if (!result.IsError)
                        {
                            if (result.Response.Succeeded)
                            {
                                return inprocAccessor;
                            }
                            else
                            {
                                return new Failure<string>(I($"Querying journal results in {result.Response.Status.ToString()}"));
                            }
                        }
                        else
                        {
                            return new Failure<string>(result.Error.Message);
                        }
                    }
                    else
                    {
                        return new Failure<string>(I($"Failed to get volume path for '{path}'"));
                    }
                }
            }
            catch (BuildXLException ex)
            {
                return new Failure<string>(ex.Message);
            }
        }
    }
}
