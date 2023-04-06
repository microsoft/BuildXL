// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tasks;
using Microsoft.Win32.SafeHandles;

namespace BuildXL.Native.IO
{
    /// <summary>
    /// Interface for file system related functions and calls
    /// </summary>
    internal interface IFileSystemExtensions
    {
        /// <summary>
        /// Flag indicating if the enlistment volume supports copy on write.
        /// </summary>
        bool IsCopyOnWriteSupportedByEnlistmentVolume { get; set; }

        /// <summary>
        /// Creates a copy on write clone of files if supported by the underlying OS.
        /// </summary>
        /// <remarks>
        /// This method must be implemented if <see cref="IsCopyOnWriteSupportedByEnlistmentVolume"/> returns true.
        /// </remarks>
        /// <exception cref="NativeWin32Exception">Throw native exception upon failure.</exception>
        Possible<Unit> CloneFile(string source, string destination, bool followSymlink);
    }
}