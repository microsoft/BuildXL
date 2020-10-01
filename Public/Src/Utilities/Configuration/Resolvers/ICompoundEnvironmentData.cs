// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.Configuration.Resolvers
{
    /// <summary>
    /// A compound environment variable value
    /// </summary>
    public interface ICompoundEnvironmentData
    {
        /// <summary>
        /// Defaults to ';' if not defined
        /// </summary>
        string Separator { get; }

        /// <nodoc/>
        IReadOnlyList<EnvironmentData> Contents { get; }
    }
}
