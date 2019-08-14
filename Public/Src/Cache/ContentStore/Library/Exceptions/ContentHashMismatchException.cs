// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;

namespace BuildXL.Cache.ContentStore.Exceptions
{
    /// <summary>
    ///     An exception that is thrown when an expected hash does match during verification.
    /// </summary>
    [SuppressMessage(
        "Microsoft.Design",
        "CA1032:ImplementStandardExceptionConstructors",
        Justification = "The exception must be constructed with content hashes")]
    [Serializable]
    public class ContentHashMismatchException : CacheException
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="ContentHashMismatchException" /> class.
        /// </summary>
        /// <param name="path">Related content file</param>
        /// <param name="actualHash">Actual content hash</param>
        /// <param name="expectedHash">Expected content hash</param>
        public ContentHashMismatchException(AbsolutePath path, ContentHash actualHash, ContentHash expectedHash)
            : base(BuildMessage(path, expectedHash, actualHash))
        {
            Contract.Requires(path != null);
        }

        private static string BuildMessage(AbsolutePath path, ContentHash expectedHash, ContentHash actualHash)
        {
            return $"Content at {path} had actual content hash {actualHash.ToShortString()} and did not match expected value of {expectedHash.ToShortString()}";
        }
    }
}
