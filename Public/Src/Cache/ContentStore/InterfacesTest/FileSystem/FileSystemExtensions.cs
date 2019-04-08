// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.FileSystem
{
    public static class FileSystemExtensions
    {
        public static AbsolutePath MakeLongPath(this IFileSystem<AbsolutePath> fileSystem, AbsolutePath root, int length)
        {
            Contract.Requires(fileSystem != null);
            Contract.Requires(root != null);
            Contract.Requires(length > root.Length + 25);

            int remainingLength = length - root.Length - 25;

            const string longPathDirectoryName = "longPath";
            const string longPathFileName = "longPathNameThatMightBeTruncatedToFitLength";

            var x = Enumerable.Repeat(longPathDirectoryName, remainingLength / (longPathDirectoryName.Length + 1));
            AbsolutePath parentDirectory = root / string.Join(@"\", x);

            if (!fileSystem.DirectoryExists(parentDirectory))
            {
                fileSystem.CreateDirectory(parentDirectory);
            }

            AbsolutePath tempPath = parentDirectory / longPathFileName.Substring(0, length - parentDirectory.Length - 1);

            Assert.Equal(length, tempPath.Length);

            return tempPath;
        }
    }
}
