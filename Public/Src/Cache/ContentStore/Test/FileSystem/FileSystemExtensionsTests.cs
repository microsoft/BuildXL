// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.FileSystem
{
    public abstract class FileSystemExtensionsTests : TestBase
    {
        protected readonly DisposableDirectory TempDirectory;

        protected FileSystemExtensionsTests(Func<IAbsFileSystem> createFileSystemFunc)
            : base(createFileSystemFunc, TestGlobal.Logger)
        {
            TempDirectory = new DisposableDirectory(FileSystem);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                TempDirectory.Dispose();
            }

            base.Dispose(disposing);
        }

        [Fact]
        public void ClearDirectorySucceeds()
        {
            var filePath1 = TempDirectory.Path / "1.txt";
            var filePath2 = TempDirectory.Path / "dir/2.txt";
            FileSystem.CreateDirectory(filePath2.Parent);
            FileSystem.WriteAllBytes(filePath1, new byte[] {1});
            FileSystem.WriteAllBytes(filePath2, new byte[] {1});
            FileSystem.ClearDirectory(filePath1.Parent, DeleteOptions.Recurse);
        }

        [Fact]
        public void ClearDirectoryOnFileThrows()
        {
            var filePath = TempDirectory.Path / "file.txt";
            FileSystem.WriteAllBytes(filePath, new byte[] {1});
            Action a = () => FileSystem.ClearDirectory(filePath, DeleteOptions.All);
            a.Should().Throw<IOException>();
        }

        [Fact]
        public void ClearDirectoryNonRecurseWithChildDirectoryThrows()
        {
            var directoryPath = TempDirectory.Path / "parent/dir";
            FileSystem.CreateDirectory(directoryPath);
            Action a = () => FileSystem.ClearDirectory(directoryPath.Parent, DeleteOptions.None);
            a.Should().Throw<IOException>();
        }
    }
}
