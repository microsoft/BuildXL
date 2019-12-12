// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;

namespace BuildXL.Native.IO
{

    /// <summary>
    /// Like <see cref="RecoverableExceptionFailure"/> except that it additionally contains 
    /// the path that could not be deleted.
    /// </summary>
    public sealed class DeletionFailure : RecoverableExceptionFailure
    {
        /// <summary>
        /// The path that could not be deleted
        /// </summary>
        public string Path { get; }

        /// <param name="path">The path that could not be deleted</param>
        /// <param name="exception">The cause of the failure</param>
        public DeletionFailure(string path, BuildXLException exception) : base(exception)
        {
            Path = path;
        }
    }
}
