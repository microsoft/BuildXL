// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities.Collections;

namespace BuildXL.Utilities.Configuration.Resolvers.Mutable
{
    /// <inheritdoc/>
    public class CompoundEnvironmentData : ICompoundEnvironmentData
    {
        /// <nodoc/>
        public const string DefaultSeparator = ";";

        /// <nodoc/>
        public CompoundEnvironmentData()
        {
            Separator = DefaultSeparator;
            Contents = CollectionUtilities.EmptyArray<EnvironmentData>();
        }

        /// <inheritdoc/>
        public string Separator { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<EnvironmentData> Contents { get; set; }
    }
}
