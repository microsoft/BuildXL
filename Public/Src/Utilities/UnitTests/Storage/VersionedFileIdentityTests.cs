// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Threading;
using BuildXL.Native.IO;
using BuildXL.Storage;
using BuildXL.Utilities;
using Microsoft.Win32.SafeHandles;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using static BuildXL.Utilities.FormattableStringEx;

namespace Test.BuildXL.Storage
{
    public sealed class VersionedFileIdentityTests : TemporaryStorageTestBase
    {
        private const string TestFileAName = "TestFileA";
        private const string TestFileBName = "TestFileB";

        [Fact]
        public void TestBasicIdentity()
        {
            string fullAPath = WriteFile(TestFileAName, TestFileAName);
            string fullBPath = WriteFile(TestFileBName, TestFileBName);

            using (var streamA = File.Open(fullAPath, FileMode.Open))
            using (var streamB = File.Open(fullBPath, FileMode.Open))
            {
                var idA = VersionedFileIdentity.TryQuery(streamA.SafeFileHandle);
                var idB = VersionedFileIdentity.TryQuery(streamB.SafeFileHandle);

                XAssert.IsTrue(idA.Succeeded);
                XAssert.IsTrue(idB.Succeeded);

                XAssert.AreEqual(idA.Result.VolumeSerialNumber, idB.Result.VolumeSerialNumber);
                XAssert.AreNotEqual(
                    idA.Result.FileId, 
                    idB.Result.FileId, 
                    I($"FileID for {TestFileAName}: {idA.Result.FileId.ToString()} -- FileID for {TestFileBName}: {idB.Result.FileId.ToString()}"));

                if (OperatingSystemHelper.IsUnixOS)
                {
                    // Due to precision, A & B may have the same version.
                    XAssert.IsTrue(idA.Result.Usn <= idB.Result.Usn);
                }
                else
                {
                    XAssert.AreNotEqual(idA.Result.Usn, idB.Result.Usn);
                    XAssert.IsTrue(idA.Result.Usn < idB.Result.Usn);
                }
            }
        }

        [Fact]
        public void TestEstablishAndQueryIdentity()
        {
            string fullPath = WriteFile(TestFileAName, TestFileAName);
            VersionedFileIdentity id1;

            using (var stream = File.Open(fullPath, FileMode.Open))
            {
                var mayBeId = VersionedFileIdentity.TryEstablishStrong(stream.SafeFileHandle, true);
                XAssert.IsTrue(mayBeId.Succeeded);
                id1 = mayBeId.Result;
            }

            VersionedFileIdentity id2;

            using (var stream = File.Open(fullPath, FileMode.Open))
            {
                var mayBeId = VersionedFileIdentity.TryQuery(stream.SafeFileHandle);
                XAssert.IsTrue(mayBeId.Succeeded);
                id2 = mayBeId.Result;
            }

            XAssert.AreEqual(id1.Usn, id2.Usn);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void TestQueryIdentityOnWriteFile(bool wait)
        {
            string fullPath = WriteFile(TestFileAName, TestFileAName);
            VersionedFileIdentity id1;

            using (var stream = File.Open(fullPath, FileMode.Open))
            {
                var mayBeId = VersionedFileIdentity.TryQuery(stream.SafeFileHandle);
                XAssert.IsTrue(mayBeId.Succeeded);
                id1 = mayBeId.Result;
            }

            if (wait)
            {
                Thread.Sleep(2000);
            }

            File.AppendAllText(fullPath, TestFileBName);

            VersionedFileIdentity id2;

            using (var stream = File.Open(fullPath, FileMode.Open))
            {
                var mayBeId = VersionedFileIdentity.TryQuery(stream.SafeFileHandle);
                XAssert.IsTrue(mayBeId.Succeeded);
                id2 = mayBeId.Result;
            }

            if (wait || !OperatingSystemHelper.IsUnixOS)
            {
                // If wait before, rewriting the file.
                XAssert.IsTrue(id1.Usn < id2.Usn);
            }
            else
            {
                XAssert.IsTrue(id1.Usn <= id2.Usn);
            }
        }

        [Fact]
        public void TestEstablishIdentityOnWriteFile()
        {
            string fullPath = WriteFile(TestFileAName, TestFileAName);
            VersionedFileIdentity id1;

            using (var stream = File.Open(fullPath, FileMode.Open))
            {
                var mayBeId = VersionedFileIdentity.TryEstablishStrong(stream.SafeFileHandle, true);
                XAssert.IsTrue(mayBeId.Succeeded);
                id1 = mayBeId.Result;
            }

            File.AppendAllText(fullPath, TestFileBName);

            VersionedFileIdentity id2;

            using (var stream = File.Open(fullPath, FileMode.Open))
            {
                var mayBeId = VersionedFileIdentity.TryQuery(stream.SafeFileHandle);
                XAssert.IsTrue(mayBeId.Succeeded);
                id2 = mayBeId.Result;
            }

            XAssert.IsTrue(id1.Usn < id2.Usn);
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public void TestSymlinkFile()
        {
            string fullPath = WriteFile(TestFileAName, TestFileAName);

            string symlinkPath1 = Path.Combine(Path.GetDirectoryName(fullPath), "sym1.link");
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(symlinkPath1, fullPath, true));

            string symlinkPath2 = Path.Combine(Path.GetDirectoryName(fullPath), "sym2.link");
            XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(symlinkPath2, fullPath, true));

            var openResult1 = FileUtilities.TryCreateOrOpenFile(
                symlinkPath1, 
                FileDesiredAccess.GenericRead, 
                FileShare.Delete | FileShare.Read, 
                FileMode.Open, 
                FileFlagsAndAttributes.FileFlagOpenReparsePoint, 
                out SafeFileHandle symlink1Handle);
            XAssert.IsTrue(openResult1.Succeeded);

            var openResult2 = FileUtilities.TryCreateOrOpenFile(
                symlinkPath2,
                FileDesiredAccess.GenericRead,
                FileShare.Delete | FileShare.Read,
                FileMode.Open,
                FileFlagsAndAttributes.FileFlagOpenReparsePoint,
                out SafeFileHandle symlink2Handle);
            XAssert.IsTrue(openResult2.Succeeded);

            using (symlink1Handle)
            using (symlink2Handle)
            {
                var id1 = VersionedFileIdentity.TryQuery(symlink1Handle);
                var id2 = VersionedFileIdentity.TryQuery(symlink2Handle);

                XAssert.IsTrue(id1.Succeeded);
                XAssert.IsTrue(id2.Succeeded);
                XAssert.AreNotEqual(id1.Result.FileId, id2.Result.FileId);
            }
        }
    }
}
