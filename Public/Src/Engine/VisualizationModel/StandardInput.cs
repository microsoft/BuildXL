// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Visualization.Models
{
    /// <summary>
    /// Standard input for pip details.
    /// </summary>
    public sealed class StandardInput
    {
        /// <summary>
        /// Standard input as file.
        /// </summary>
        public FileReference File { get; }

        /// <summary>
        /// Standard input as data.
        /// </summary>
        public IEnumerable<string> Data { get; }

        /// <summary>
        /// Invalid standard input.
        /// </summary>
        public static readonly StandardInput Invalid = new StandardInput(null, null);

        private StandardInput(FileReference file, IEnumerable<string> data)
        {
            File = file;
            Data = data;
        }

        /// <summary>
        /// Creates a standard input from a file reference.
        /// </summary>
        public static StandardInput CreateFromFile(FileReference file)
        {
            Contract.Requires(file != null);
            return new StandardInput(file, null);
        }

        /// <summary>
        /// Creates a standard input from data.
        /// </summary>
        public static StandardInput CreateFromData(IEnumerable<string> data)
        {
            Contract.Requires(data != null);
            return new StandardInput(null, data);
        }
    }
}
