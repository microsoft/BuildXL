// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using BuildXL.Native.IO;
using BuildXL.Storage;
using BuildXL.Storage.ChangeJournalService;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.VmCommandProxy;

namespace Test.BuildXL.TestUtilities.Xunit
{
    /// <summary>
    /// Journal utilities for unit tests.
    /// </summary>
    public static class JournalUtils
    {
        /// <summary>
        /// Gets an instance of <see cref="IChangeJournalAccessor"/> for tests.
        /// </summary>
        /// <param name="volumeMap">Volume map.</param>
        /// <returns>An instance of <see cref="IChangeJournalAccessor"/>.</returns>
        public static Possible<IChangeJournalAccessor> TryGetJournalAccessorForTest(VolumeMap volumeMap)
        {
            string path = FileUtilities.GetTempFileName();

            var maybeJournal = JournalAccessorGetter.TryGetJournalAccessor(volumeMap, path);

            FileUtilities.DeleteFile(path);

            return maybeJournal;
        }

        /// <summary>
        /// Creates a map of local volumes.
        /// </summary>
        public static VolumeMap TryCreateMapOfAllLocalVolumes(LoggingContext loggingContext, IReadOnlyList<string> junctionRoots = null)
        {
            var volumeMap = VolumeMap.CreateMapOfAllLocalVolumes(loggingContext, junctionRoots);

            // We want to skip volumes that are not local to VM.
            volumeMap.SkipTrackingJournalIncapableVolume = HasRelocatedTempInVm;

            return volumeMap;
        }

        private static bool HasRelocatedTempInVm => VmSpecialEnvironmentVariables.HasRelocatedTemp;
    }
}
