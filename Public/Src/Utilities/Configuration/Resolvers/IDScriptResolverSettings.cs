// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Utilities;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Configuration for source resolver.
    /// </summary>
    /// <remarks>
    /// For now, a source resolver is assumed to be DScript resolver.
    /// A package is a DScript specification package.dsc and its descriptor package.config.dsc
    /// that is adjacent to the package.dsc itself.
    /// TODO: Feel free to move it to the appropriate place.
    /// </remarks>
    public partial interface IDScriptResolverSettings : IResolverSettings
    {
        /// <summary>
        /// The directory where the resolver starts collecting all packages (including all sub-directories recursively).
        /// </summary>
        /// <remarks>
        /// In the past the resolver only considers one-level down of sub-directories. This has created
        /// a confusion about the meaning of the root. We can discuss further what the best practice.
        /// </remarks>
        AbsolutePath Root { get; }

        /// <summary>
        /// Paths to modules, i.e., paths to project files or folders containing package.dsc.
        /// </summary>
        IReadOnlyList<AbsolutePath> Modules { get; }

        /// <summary>
        /// Paths to packages. Legacy field, see <see cref="Modules"/>
        /// </summary>
        IReadOnlyList<AbsolutePath> Packages { get; }
    }
}
