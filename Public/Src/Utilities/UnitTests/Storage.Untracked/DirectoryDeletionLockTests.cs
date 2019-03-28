// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using BuildXL.Storage;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Storage
{
    public class DirectoryDeletionLockTests : TemporaryStorageTestBase
    {
        // Enable this for CoreCLR builds as soon as we have a cross platform way of doing sane directory deletion locking
        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void CannotDeleteLockedDirectory()
        {
            string dir = Path.Combine(TemporaryDirectory, "DeleteMe");

            using (var dirLock = new DirectoryDeletionLock())
            {
                dirLock.CreateAndPreventDeletion(dir);

                try
                {
                    Directory.Delete(dir);
                }
                catch (IOException)
                {
                    return;
                }

                XAssert.Fail("Expected a sharing violation");
            }
        }

        [Fact]
        public void CanDeleteDirectoryAfterUnlocking()
        {
            string dir = Path.Combine(TemporaryDirectory, "DeleteMe");

            using (var dirLock = new DirectoryDeletionLock())
            {
                dirLock.CreateAndPreventDeletion(dir);
            }

            Directory.Delete(dir);
        }

        [Fact]
        public void RedundantCreateWhileLocked()
        {
            string dir = Path.Combine(TemporaryDirectory, "DeleteMe");

            using (var dirLock = new DirectoryDeletionLock())
            {
                dirLock.CreateAndPreventDeletion(dir);
                Directory.CreateDirectory(dir);
            }
        }
    }
}
