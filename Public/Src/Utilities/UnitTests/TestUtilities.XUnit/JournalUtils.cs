// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BuildXL.Native.IO;
using BuildXL.Storage;
using BuildXL.Storage.ChangeJournalService;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;

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
            string path = null;
            bool usingTempFile = false;

            if (!IsInVm)
            {
                path = AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly());
            }
            else
            {
                path = Path.GetTempFileName();
                usingTempFile = true;
            }

            var maybeJournal = JournalAccessorGetter.TryGetJournalAccessor(volumeMap, path);

            if (usingTempFile)
            {
                FileUtilities.DeleteFile(path);
            }

            return maybeJournal;
        }

        /// <summary>
        /// Creates a map of local volumes.
        /// </summary>
        public static VolumeMap TryCreateMapOfAllLocalVolumes(LoggingContext loggingContext, IReadOnlyList<string> junctionRoots = null)
        {
            var volumeMap = VolumeMap.TryCreateMapOfAllLocalVolumes(loggingContext, junctionRoots);

            // We want to skip volumes that are not local to VM.
            volumeMap.SkipTrackingJournalIncapableVolume = IsInVm;

            return volumeMap;
        }

        private static bool IsInVm => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("[BUILDXL]VM_TEMP"));
    }
}
