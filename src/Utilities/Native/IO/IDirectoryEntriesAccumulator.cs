// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using BuildXL.Utilities;

#pragma warning disable SA1649 // File name should match first type name    

namespace BuildXL.Native.IO
{
    /// <summary>
    /// Class for representing a directory entry accumulator.
    /// </summary>
    public interface IDirectoryEntryAccumulator
    {      
        /// <summary>
        /// Directory path.
        /// </summary>
        AbsolutePath DirectoryPath { get; }

        /// <summary>
        /// Flag indicating if enumeration is successful.
        /// </summary>
        bool Succeeded { get; set; }

        /// <summary>
        /// Adds file name under <see cref="DirectoryPath"/>.
        /// </summary>
        /// <remarks>
        /// The added file name has been filtered by path match spec.
        /// </remarks>
        void AddFile(string fileName);

        /// <summary>
        /// Adds file and its attribute for input tracking purpose.
        /// </summary>
        void AddTrackFile(string fileName, FileAttributes fileAttributes);
    }

    /// <summary>
    /// Class for accumulating directory entries during their enumeration.
    /// </summary>
    public interface IDirectoryEntriesAccumulator
    {
        /// <summary>
        /// Current directory entry accumulator.
        /// </summary>
        IDirectoryEntryAccumulator Current { get; }

        /// <summary>
        /// Adds a new directory entry accumulator given only directory name, not directory path, and then push it to the stack.
        /// </summary>
        void AddNew(IDirectoryEntryAccumulator parent, string directoryName);
        
        /// <summary>
        /// Indicates that no modification can be done further.
        /// </summary>
        void Done();
    }
}

#pragma warning restore SA1649
