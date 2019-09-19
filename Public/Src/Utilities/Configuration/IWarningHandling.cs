// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using JetBrains.Annotations;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Interface for generic warning handling.
    /// </summary>
    public interface IWarningHandling
    {
        /// <summary>
        /// Treat warnings as errors
        /// </summary>
        bool TreatWarningsAsErrors { get; }

        /// <summary>
        /// Explit list of warnings that should be errors
        /// </summary>
        [JetBrains.Annotations.NotNull]
        IReadOnlyList<int> WarningsAsErrors { get; }

        /// <summary>
        /// Warnings that explicitly should not be treated as errors.
        /// </summary>
        [JetBrains.Annotations.NotNull]
        IReadOnlyList<int> WarningsNotAsErrors { get; }

        /// <summary>
        /// Warnings to suppress
        /// </summary>
        [JetBrains.Annotations.NotNull]
        IReadOnlyList<int> NoWarnings { get; }
    }
}
