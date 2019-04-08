// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using JetBrains.Annotations;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Qualifier settings
    /// </summary>
    public interface IQualifierConfiguration
    {
        /// <summary>
        /// The command line default qualifier to use in the build
        /// </summary>
        [NotNull]
        IReadOnlyDictionary<string, string> DefaultQualifier { get; }

        /// <summary>
        /// A set of named qualifiers as convenient for the commandline build.
        /// </summary>
        [NotNull]
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> NamedQualifiers { get; }
    }
}
