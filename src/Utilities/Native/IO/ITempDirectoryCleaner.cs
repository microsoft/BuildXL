// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Native.IO
{
    /// <summary>
    /// An interface for classes that provide and clean a temp directory.
    /// Some attempt to clean <see cref="TempDirectory"/> should occur by the time 
    /// class instance is disposed. 
    /// </summary>
    public interface ITempDirectoryCleaner : IDisposable
    {
        /// <summary>
        /// Returns the path to a temporary directory owned and cleaned by the implementor
        /// </summary>
        string TempDirectory { get; }
    }
}
