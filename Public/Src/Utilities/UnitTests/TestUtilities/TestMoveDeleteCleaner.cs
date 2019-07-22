// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Threading;
using BuildXL.Native.IO;

namespace Test.BuildXL.TestUtilities
{
    /// <summary>
    /// This is minimal implementation of <see cref="ITempCleaner"/> for unit tests or test bases
    /// This can be passed into <see cref="FileUtilities.DeleteDirectoryContents(string, bool, System.Func{string, bool}, ITempCleaner, CancellationToken?)"/>
    /// and <see cref="FileUtilities.DeleteFile(string, bool, ITempCleaner)"/> to enable move-deletes,
    /// which are more reliable and less prone to unexected exceptions than Windows delete.
    /// </summary>
    public class TestMoveDeleteCleaner : ITempCleaner
    {
        /// <summary>
        /// Suggested name for <see cref="TempDirectory"/>
        /// </summary>
        public const string MoveDeleteDirectoryName = "MoveDeleteTemp";

        /// <summary>
        /// Constructor
        /// </summary>
        public TestMoveDeleteCleaner(string tempDirectory)
        {
            Directory.CreateDirectory(tempDirectory);
            TempDirectory = tempDirectory;
        }

        /// <inheritdoc />
        public string TempDirectory { get; private set; }

        /// <summary>
        /// Cleans <see cref="TempDirectory"/>
        /// </summary>
        public void Dispose()
        {
            FileUtilities.DeleteDirectoryContents(TempDirectory, deleteRootDirectory: true);
        }

        /// <inheritdoc />
        public void RegisterDirectoryToDelete(string path, bool deleteRootDirectory)
        {
            // noop
        }

        /// <inheritdoc />
        public void RegisterFileToDelete(string path)
        {
            // noop
        }
    }
}
