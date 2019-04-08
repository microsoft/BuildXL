// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Native.IO;
using BuildXL.Native.IO.Windows;
using BuildXL.Storage;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

#pragma warning disable AsyncFixer02

namespace Test.BuildXL.Storage
{

    public class FileContentTableExtensionsTests : TemporaryStorageTestBase
    {
        private const string FileA = "A";
        private const string FileB = "B";

        private async Task VerifyCopyIfContentMismatchedAsync(
            FileContentTable fileContentTable,
            string sourceRelPath,
            string destRelPath,
            FileContentInfo sourceVersionedHash,
            bool expectedCopy,
            VersionedFileIdentityAndContentInfo? originalDestinationInfo = null)
        {
            ConditionalUpdateResult result =
                await fileContentTable.CopyIfContentMismatchedAsync(GetFullPath(sourceRelPath), GetFullPath(destRelPath), sourceVersionedHash);

            XAssert.AreEqual(expectedCopy, !result.Elided, "Decision to copy or elide was incorrect.");

            FileContentTableExtensions.VersionedFileIdentityAndContentInfoWithOrigin targetInfo =
                await fileContentTable.GetAndRecordContentHashAsync(GetFullPath(destRelPath));
            XAssert.AreEqual(FileContentTableExtensions.ContentHashOrigin.Cached, targetInfo.Origin, "Copy didn't record the target hash.");
            XAssert.AreEqual(
                sourceVersionedHash.Hash,
                targetInfo.VersionedFileIdentityAndContentInfo.FileContentInfo.Hash,
                "Hashes should match after the copy.");
            XAssert.AreEqual(
                File.ReadAllText(GetFullPath(sourceRelPath)),
                File.ReadAllText(GetFullPath(destRelPath)),
                "Hashes match but content does not");

            if (originalDestinationInfo.HasValue)
            {
                VerifyDestinationVersion(result, originalDestinationInfo.Value);
            }
        }

        private async Task VerifyWriteBytesIfContentMismatchedAsync(
            FileContentTable fileContentTable,
            string targetRelPath,
            string contents,
            bool expectWrite,
            VersionedFileIdentityAndContentInfo? originalDestinationInfo = null)
        {
            byte[] encoded = Encoding.UTF8.GetBytes(contents);
            ContentHash contentsHash = ContentHashingUtilities.HashBytes(encoded);
            ConditionalUpdateResult result =
                await fileContentTable.WriteBytesIfContentMismatchedAsync(GetFullPath(targetRelPath), encoded, contentsHash);

            XAssert.AreEqual(expectWrite, !result.Elided, "Decision to write was incorrect.");

            FileContentTableExtensions.VersionedFileIdentityAndContentInfoWithOrigin targetInfo =
                await fileContentTable.GetAndRecordContentHashAsync(GetFullPath(targetRelPath));
            XAssert.AreEqual(FileContentTableExtensions.ContentHashOrigin.Cached, targetInfo.Origin, "Write didn't record the target hash.");
            XAssert.AreEqual(
                contentsHash,
                targetInfo.VersionedFileIdentityAndContentInfo.FileContentInfo.Hash,
                "Hashes should match after the write.");
            XAssert.AreEqual(contents, File.ReadAllText(GetFullPath(targetRelPath)), "Hashes match but content does not");

            if (originalDestinationInfo.HasValue)
            {
                VerifyDestinationVersion(result, originalDestinationInfo.Value);
            }
        }

        private void VerifyDestinationVersion(ConditionalUpdateResult updateResult, VersionedFileIdentityAndContentInfo originalDestinationInfo)
        {
            if (!updateResult.Elided)
            {
                XAssert.AreNotEqual(
                    originalDestinationInfo.FileContentInfo.Hash,
                    updateResult.DestinationInfo.FileContentInfo.Hash,
                    "The copy / write should have changed the hash");
                XAssert.AreNotEqual(originalDestinationInfo.FileContentInfo, updateResult.DestinationInfo.FileContentInfo);
                XAssert.AreNotEqual(
                    originalDestinationInfo.Identity.ToWeakIdentity(),
                    updateResult.DestinationInfo.Identity.ToWeakIdentity(),
                    "Expected an identity change due to the copy");
                XAssert.AreNotEqual(originalDestinationInfo, updateResult.DestinationInfo);
            }
            else
            {
                XAssert.AreEqual(originalDestinationInfo.FileContentInfo.Hash, updateResult.DestinationInfo.FileContentInfo.Hash);
                XAssert.AreEqual(originalDestinationInfo.FileContentInfo, updateResult.DestinationInfo.FileContentInfo);
                XAssert.AreEqual(
                    originalDestinationInfo.Identity.ToWeakIdentity(),
                    updateResult.DestinationInfo.Identity.ToWeakIdentity(),
                    "Expected identity to stay the same due to copy / write elision");
            }
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void ContentHashWithOriginEquality()
        {
            ContentHash hashA = ContentHashingUtilities.CreateRandom();
            ContentHash hashB = ContentHashingUtilities.CreateRandom();

            FileContentInfo contentInfoA = new FileContentInfo(hashA, 123);
            FileContentInfo contentInfoB = new FileContentInfo(hashB, 123);

            VersionedFileIdentity identityA = new VersionedFileIdentity(
                volumeSerialNumber: 1,
                fileId: new FileId(2, 3),
                usn: new Usn(4),
                kind: VersionedFileIdentity.IdentityKind.StrongUsn);
            VersionedFileIdentity identityB = new VersionedFileIdentity(
                volumeSerialNumber: 2,
                fileId: new FileId(3, 4),
                usn: new Usn(5),
                kind: VersionedFileIdentity.IdentityKind.StrongUsn);

            StructTester.TestEquality(
                new FileContentTableExtensions.VersionedFileIdentityAndContentInfoWithOrigin(
                    new VersionedFileIdentityAndContentInfo(identityA, contentInfoA),
                    FileContentTableExtensions.ContentHashOrigin.NewlyHashed),
                equalValue: new FileContentTableExtensions.VersionedFileIdentityAndContentInfoWithOrigin(
                    new VersionedFileIdentityAndContentInfo(identityA, contentInfoA),
                    FileContentTableExtensions.ContentHashOrigin.NewlyHashed),
                notEqualValues: new[]
                                {
                                    new FileContentTableExtensions.VersionedFileIdentityAndContentInfoWithOrigin(
                                        new VersionedFileIdentityAndContentInfo(identityA, contentInfoA),
                                        FileContentTableExtensions.ContentHashOrigin.Cached),
                                    new FileContentTableExtensions.VersionedFileIdentityAndContentInfoWithOrigin(
                                        new VersionedFileIdentityAndContentInfo(identityB, contentInfoA),
                                        FileContentTableExtensions.ContentHashOrigin.NewlyHashed),
                                    new FileContentTableExtensions.VersionedFileIdentityAndContentInfoWithOrigin(
                                        new VersionedFileIdentityAndContentInfo(identityA, contentInfoB),
                                        FileContentTableExtensions.ContentHashOrigin.NewlyHashed),
                                },
                eq: (a, b) => a == b,
                neq: (a, b) => a != b);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public async Task CopyIfContentMismatchedAsyncBidirectionalFixpoint()
        {
            const string TargetContent = "Target!!!!!!!!!";

            var fileContentTable = FileContentTable.CreateNew();
            WriteFile(FileA, TargetContent);

            FileContentTableExtensions.VersionedFileIdentityAndContentInfoWithOrigin sourceInfo =
                await fileContentTable.GetAndRecordContentHashAsync(GetFullPath(FileA));
            XAssert.AreEqual(FileContentTableExtensions.ContentHashOrigin.NewlyHashed, sourceInfo.Origin);

            await
                VerifyCopyIfContentMismatchedAsync(
                    fileContentTable,
                    FileA,
                    FileB,
                    sourceInfo.VersionedFileIdentityAndContentInfo.FileContentInfo,
                    expectedCopy: true);
            await
                VerifyCopyIfContentMismatchedAsync(
                    fileContentTable,
                    FileB,
                    FileA,
                    sourceInfo.VersionedFileIdentityAndContentInfo.FileContentInfo,
                    expectedCopy: false);
            await
                VerifyCopyIfContentMismatchedAsync(
                    fileContentTable,
                    FileA,
                    FileB,
                    sourceInfo.VersionedFileIdentityAndContentInfo.FileContentInfo,
                    expectedCopy: false);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public async Task CopyIfContentMismatchedAsyncFixpoint()
        {
            const string TargetContent = "Target!!!!!!!!!";

            var fileContentTable = FileContentTable.CreateNew();
            WriteFile(FileA, TargetContent);

            FileContentTableExtensions.VersionedFileIdentityAndContentInfoWithOrigin sourceInfo =
                await fileContentTable.GetAndRecordContentHashAsync(GetFullPath(FileA));
            XAssert.AreEqual(FileContentTableExtensions.ContentHashOrigin.NewlyHashed, sourceInfo.Origin);

            await
                VerifyCopyIfContentMismatchedAsync(
                    fileContentTable,
                    FileA,
                    FileB,
                    sourceInfo.VersionedFileIdentityAndContentInfo.FileContentInfo,
                    expectedCopy: true);
            await
                VerifyCopyIfContentMismatchedAsync(
                    fileContentTable,
                    FileA,
                    FileB,
                    sourceInfo.VersionedFileIdentityAndContentInfo.FileContentInfo,
                    expectedCopy: false);
            await
                VerifyCopyIfContentMismatchedAsync(
                    fileContentTable,
                    FileA,
                    FileB,
                    sourceInfo.VersionedFileIdentityAndContentInfo.FileContentInfo,
                    expectedCopy: false);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public async Task CopyIfContentMismatchedAsyncWithAbsentDestination()
        {
            const string TargetContent = "Target!!!!!!!!!";

            var fileContentTable = FileContentTable.CreateNew();
            WriteFile(FileA, TargetContent);

            FileContentTableExtensions.VersionedFileIdentityAndContentInfoWithOrigin sourceInfo =
                await fileContentTable.GetAndRecordContentHashAsync(GetFullPath(FileA));
            XAssert.AreEqual(FileContentTableExtensions.ContentHashOrigin.NewlyHashed, sourceInfo.Origin);

            await
                VerifyCopyIfContentMismatchedAsync(
                    fileContentTable,
                    FileA,
                    FileB,
                    sourceInfo.VersionedFileIdentityAndContentInfo.FileContentInfo,
                    expectedCopy: true);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public async Task CopyIfContentMismatchedAsyncWithMatchingDestination()
        {
            const string TargetContent = "Target!!!!!!!!!";

            var fileContentTable = FileContentTable.CreateNew();
            WriteFile(FileA, TargetContent);
            WriteFile(FileB, TargetContent);

            FileContentTableExtensions.VersionedFileIdentityAndContentInfoWithOrigin sourceInfo =
                await fileContentTable.GetAndRecordContentHashAsync(GetFullPath(FileA));
            XAssert.AreEqual(FileContentTableExtensions.ContentHashOrigin.NewlyHashed, sourceInfo.Origin);

            FileContentTableExtensions.VersionedFileIdentityAndContentInfoWithOrigin targetInfo =
                await fileContentTable.GetAndRecordContentHashAsync(GetFullPath(FileB));
            XAssert.AreEqual(FileContentTableExtensions.ContentHashOrigin.NewlyHashed, targetInfo.Origin);

            XAssert.AreEqual(
                sourceInfo.VersionedFileIdentityAndContentInfo.FileContentInfo.Hash,
                targetInfo.VersionedFileIdentityAndContentInfo.FileContentInfo.Hash);

            await
                VerifyCopyIfContentMismatchedAsync(
                    fileContentTable,
                    FileA,
                    FileB,
                    sourceInfo.VersionedFileIdentityAndContentInfo.FileContentInfo,
                    expectedCopy: false,
                    originalDestinationInfo: targetInfo.VersionedFileIdentityAndContentInfo);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public async Task CopyIfContentMismatchedAsyncWithMismatchedDestination()
        {
            const string TargetContent = "Target!!!!!!!!!";

            var fileContentTable = FileContentTable.CreateNew();
            WriteFile(FileA, TargetContent);
            WriteFile(FileB, "Nope");

            FileContentTableExtensions.VersionedFileIdentityAndContentInfoWithOrigin sourceInfo =
                await fileContentTable.GetAndRecordContentHashAsync(GetFullPath(FileA));
            XAssert.AreEqual(FileContentTableExtensions.ContentHashOrigin.NewlyHashed, sourceInfo.Origin);

            FileContentTableExtensions.VersionedFileIdentityAndContentInfoWithOrigin targetInfo =
                await fileContentTable.GetAndRecordContentHashAsync(GetFullPath(FileB));
            XAssert.AreEqual(FileContentTableExtensions.ContentHashOrigin.NewlyHashed, targetInfo.Origin);

            XAssert.AreNotEqual(
                sourceInfo.VersionedFileIdentityAndContentInfo.FileContentInfo.Hash,
                targetInfo.VersionedFileIdentityAndContentInfo.FileContentInfo.Hash);

            await
                VerifyCopyIfContentMismatchedAsync(
                    fileContentTable,
                    FileA,
                    FileB,
                    sourceInfo.VersionedFileIdentityAndContentInfo.FileContentInfo,
                    expectedCopy: true,
                    originalDestinationInfo: targetInfo.VersionedFileIdentityAndContentInfo);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public async Task CopyIfContentMismatchedAsyncWithUnknwonDestination()
        {
            const string TargetContent = "Target!!!!!!!!!";

            var fileContentTable = FileContentTable.CreateNew();
            WriteFile(FileA, TargetContent);
            WriteFile(FileB, "Nope");

            FileContentTableExtensions.VersionedFileIdentityAndContentInfoWithOrigin sourceInfo =
                await fileContentTable.GetAndRecordContentHashAsync(GetFullPath(FileA));
            XAssert.AreEqual(FileContentTableExtensions.ContentHashOrigin.NewlyHashed, sourceInfo.Origin);

            await
                VerifyCopyIfContentMismatchedAsync(
                    fileContentTable,
                    FileA,
                    FileB,
                    sourceInfo.VersionedFileIdentityAndContentInfo.FileContentInfo,
                    expectedCopy: true);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public async Task GetAndRecordContentHashAsyncThrowsWithMissingFIle()
        {
            var fileContentTable = FileContentTable.CreateNew();

            try
            {
                await fileContentTable.GetAndRecordContentHashAsync(GetFullPath(FileA));
            }
            catch (BuildXLException)
            {
                return;
            }

            XAssert.Fail("Excepted an exception due to a missing file to read");
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public async Task GetAndRecordContentHashAsyncWithEmptyFileContentTable()
        {
            var fileContentTable = FileContentTable.CreateNew();
            WriteFile(FileA, "Some string");

            FileContentTableExtensions.VersionedFileIdentityAndContentInfoWithOrigin result =
                await fileContentTable.GetAndRecordContentHashAsync(GetFullPath(FileA));
            XAssert.AreEqual(FileContentTableExtensions.ContentHashOrigin.NewlyHashed, result.Origin);
            XAssert.AreEqual(
                await ContentHashingUtilities.HashFileAsync(GetFullPath(FileA)),
                result.VersionedFileIdentityAndContentInfo.FileContentInfo.Hash);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public async Task GetAndRecordContentHashAsyncWithMatch()
        {
            ContentHash fakeHash = ContentHashingUtilities.CreateRandom();

            var fileContentTable = FileContentTable.CreateNew();
            WriteFile(FileA, "Some string");

            using (FileStream fs = File.OpenRead(GetFullPath(FileA)))
            {
                // Note that this content hash is clearly wrong, but also not all zeroes.
                fileContentTable.RecordContentHash(fs, fakeHash);
            }

            FileContentTableExtensions.VersionedFileIdentityAndContentInfoWithOrigin result =
                await fileContentTable.GetAndRecordContentHashAsync(GetFullPath(FileA));
            XAssert.AreEqual(FileContentTableExtensions.ContentHashOrigin.Cached, result.Origin);
            XAssert.AreEqual(fakeHash, result.VersionedFileIdentityAndContentInfo.FileContentInfo.Hash);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public async Task GetAndRecordContentHashAsyncWithMismatch()
        {
            var fileContentTable = FileContentTable.CreateNew();

            using (FileStream fs = File.Open(GetFullPath(FileA), FileMode.CreateNew, FileAccess.Write))
            {
                // Register an empty file.
                fileContentTable.RecordContentHash(fs, ContentHashingUtilities.EmptyHash);

                byte[] content = Encoding.UTF8.GetBytes("Some new content");
                await fs.WriteAsync(content, 0, content.Length);
            }

            FileContentTableExtensions.VersionedFileIdentityAndContentInfoWithOrigin result =
                await fileContentTable.GetAndRecordContentHashAsync(GetFullPath(FileA));
            XAssert.AreEqual(FileContentTableExtensions.ContentHashOrigin.NewlyHashed, result.Origin);
            XAssert.AreEqual(
                await ContentHashingUtilities.HashFileAsync(GetFullPath(FileA)),
                result.VersionedFileIdentityAndContentInfo.FileContentInfo.Hash);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public async Task WriteBytesIfContentMismatchedAsyncFixpoint()
        {
            const string TargetContent = "Target!!!!!!!!!";
            var fileContentTable = FileContentTable.CreateNew();

            await VerifyWriteBytesIfContentMismatchedAsync(fileContentTable, FileA, TargetContent, expectWrite: true);
            await VerifyWriteBytesIfContentMismatchedAsync(fileContentTable, FileA, TargetContent, expectWrite: false);
            await VerifyWriteBytesIfContentMismatchedAsync(fileContentTable, FileA, TargetContent, expectWrite: false);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public Task WriteBytesIfContentMismatchedAsyncWithAbsentDestination()
        {
            const string TargetContent = "Target!!!!!!!!!";

            var fileContentTable = FileContentTable.CreateNew();

            return VerifyWriteBytesIfContentMismatchedAsync(fileContentTable, FileB, TargetContent, expectWrite: true);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public async Task WriteBytesIfContentMismatchedAsyncWithMatchingDestination()
        {
            const string TargetContent = "Target!!!!!!!!!";

            var fileContentTable = FileContentTable.CreateNew();
            WriteFile(FileB, TargetContent);

            FileContentTableExtensions.VersionedFileIdentityAndContentInfoWithOrigin targetInfo =
                await fileContentTable.GetAndRecordContentHashAsync(GetFullPath(FileB));
            XAssert.AreEqual(FileContentTableExtensions.ContentHashOrigin.NewlyHashed, targetInfo.Origin);

            await
                VerifyWriteBytesIfContentMismatchedAsync(
                    fileContentTable,
                    FileB,
                    TargetContent,
                    expectWrite: false,
                    originalDestinationInfo: targetInfo.VersionedFileIdentityAndContentInfo);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public async Task WriteBytesIfContentMismatchedAsyncWithMismatchedDestination()
        {
            const string TargetContent = "Target!!!!!!!!!";

            var fileContentTable = FileContentTable.CreateNew();
            WriteFile(FileB, "Nope");

            FileContentTableExtensions.VersionedFileIdentityAndContentInfoWithOrigin targetInfo =
                await fileContentTable.GetAndRecordContentHashAsync(GetFullPath(FileB));
            XAssert.AreEqual(FileContentTableExtensions.ContentHashOrigin.NewlyHashed, targetInfo.Origin);

            await
                VerifyWriteBytesIfContentMismatchedAsync(
                    fileContentTable,
                    FileB,
                    TargetContent,
                    expectWrite: true,
                    originalDestinationInfo: targetInfo.VersionedFileIdentityAndContentInfo);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public Task WriteBytesIfContentMismatchedAsyncWithUnknwonDestination()
        {
            const string TargetContent = "Target!!!!!!!!!";

            var fileContentTable = FileContentTable.CreateNew();
            WriteFile(FileB, "Nope");

            return VerifyWriteBytesIfContentMismatchedAsync(fileContentTable, FileB, TargetContent, expectWrite: true);
        }
    }
}

#pragma warning restore AsyncFixer02
