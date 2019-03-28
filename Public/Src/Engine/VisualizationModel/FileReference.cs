// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;

namespace BuildXL.Visualization.Models
{
    /// <summary>
    /// Viewmodel of a reference to a file
    /// </summary>
    public class FileReference
    {
        /// <summary>
        /// File Id
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Path
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// Creates a new FileReference from a file
        /// </summary>
        public static FileReference FromAbsolutePath(PathTable pathTable, AbsolutePath path)
        {
            Contract.Requires(pathTable != null);

            return new FileReference
                   {
                       Id = path.Value.Value,
                       Path = path.ToString(pathTable),
                   };
        }

        /// <summary>
        /// Creates a new FileReference from a spec file
        /// </summary>
        public static FileReference FromSpecFile(PathTable pathTable, SpecFilePip specFile)
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(specFile != null);

            return new FileReference
            {
                Id = (int)specFile.PipId.Value,
                Path = specFile.SpecFile.Path.ToString(pathTable),
            };
        }
    }
}
