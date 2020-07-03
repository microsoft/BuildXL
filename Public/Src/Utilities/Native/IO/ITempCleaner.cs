// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Native.IO
{
    /// <summary>
    /// An interface for classes that provide and clean a temp directory.
    /// Some attempt to clean <see cref="TempDirectory"/> should occur by the time 
    /// class instance is disposed. 
    /// </summary>
    public interface ITempCleaner : IDisposable
    {
        /// <summary>
        /// Returns the path to a temporary directory owned and cleaned by the implementor
        /// </summary>
        string TempDirectory { get; }


        /// <summary>
        /// Registers a file to delete.
        /// </summary>
        void RegisterFileToDelete(string path);

        /// <summary>
        /// Registers a directory to delete.
        /// </summary>
        /// <remarks>
        /// The contents of registered directory will be deleted. The directory itself will be deleted
        /// if <paramref name="deleteRootDirectory"/> is true.
        /// </remarks>
        void RegisterDirectoryToDelete(string path, bool deleteRootDirectory);
    }
}
