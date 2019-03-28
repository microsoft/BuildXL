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
    public sealed class PassThroughFileSystemExtensionsTests : FileSystemExtensionsTests
    {
        public PassThroughFileSystemExtensionsTests()
            : base(() => new PassThroughFileSystem(TestGlobal.Logger))
        {
        }

        [Fact]
        [Trait("Category", "WindowsOSOnly")] // We do not block deletion on MacOS
        public void ClearDirectoryWithReadOnlyFileThrowsWithAppropriateError()
        {        
            var filePath = TempDirectory.Path / "dir" / "file.txt";
            FileSystem.CreateDirectory(filePath.Parent);
            FileSystem.WriteAllBytes(filePath, new byte[] { 1 });
            FileSystem.SetFileAttributes(filePath, FileAttributes.ReadOnly);
            FileSystem.DenyFileWrites(filePath);
            // New file deletion logic based on BuildXL.Native allows us to clean up directories with read only files in it.
            Action a = () => FileSystem.ClearDirectory(filePath.Parent, DeleteOptions.None);
            a();
        }

        [Fact]
        public void ClearDirectoryWithReadOnlyFileDoesNotThrow()
        {
            var filePath = TempDirectory.Path / "dir/file.txt";
            FileSystem.CreateDirectory(filePath.Parent);
            FileSystem.WriteAllBytes(filePath, new byte[] { 1 });
            FileSystem.SetFileAttributes(filePath, FileAttributes.ReadOnly);
            FileSystem.DenyFileWrites(filePath);
            // New file deletion logic based on BuildXL.Native allows us to clean up directories with read only files in it.
            Action a = () => FileSystem.ClearDirectory(filePath.Parent, DeleteOptions.None);
            a();
        }

    }
}
