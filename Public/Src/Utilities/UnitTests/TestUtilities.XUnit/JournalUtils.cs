// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Reflection;
using BuildXL.Native.IO;
using BuildXL.Storage;
using BuildXL.Storage.ChangeJournalService;
using BuildXL.Utilities;

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

            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("[BUILDXL]VM_TEMP")))
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
    }
}
