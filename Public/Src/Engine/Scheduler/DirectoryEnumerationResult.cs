// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Native.IO;
using BuildXL.Utilities;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Directory information that is cached during the build
    /// </summary>
    public readonly struct DirectoryEnumerationResult
    {
        /// <summary>
        /// Existence of the directory
        /// </summary>
        public readonly PathExistence Existence;

        /// <summary>
        /// Members of the directory
        /// </summary>
        public readonly IReadOnlyList<(AbsolutePath, string)> Members;

        /// <summary>
        /// Whether the directory is valid
        /// </summary>
        /// <remarks>
        /// The directory will be invalid if BuildXL fails to check the existence of the directory. 
        /// </remarks>
        public bool IsValid => Members != null;

        /// <summary>
        /// Invalid DirectoryContents which BuildXL fails to check the existence
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2211:NonConstantFieldsShouldNotBeVisible")]
        public static DirectoryEnumerationResult Invalid = default(DirectoryEnumerationResult);

        /// <summary>
        /// Constructor
        /// </summary>
        public DirectoryEnumerationResult(PathExistence existence, IReadOnlyList<(AbsolutePath, string)> members)
        {
            Contract.Requires(members != null);
            Contract.Requires(members.Count == 0 || existence == PathExistence.ExistsAsDirectory);

            Existence = existence;
            Members = members;
        }
    }
}
