// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// Provenance for pip graph fragment.
    /// </summary>
    public sealed class PipGraphFragmentProvenance
    {
        /// <summary>
        /// Path of pip graph fragment.
        /// </summary>
        public AbsolutePath Path { get; private set; }

        /// <summary>
        /// Description of pip graph fragment.
        /// </summary>
        public string Description { get; private set; }

        /// <summary>
        /// Creates an instance of <see cref="PipGraphFragmentProvenance"/>
        /// </summary>
        public PipGraphFragmentProvenance(AbsolutePath path, string description)
        {
            Contract.Requires(path.IsValid);
            Path = path;
            Description = description ?? string.Empty;
        }
    }
}
