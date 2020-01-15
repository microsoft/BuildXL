// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// The information needed to generate msbuild files
    /// </summary>
    public interface IIdeConfiguration
    {
        /// <summary>
        /// Whether the Ide generator is enabled or not.
        /// </summary>
        bool IsEnabled { get; }

        /// <summary>
        /// Whether the Ide generator generates msbuild project files under the source tree
        /// </summary>
        bool? CanWriteToSrc { get; }

        /// <summary>
        /// Solution file name that will be generated
        /// </summary>
        PathAtom SolutionName { get; }

        /// <summary>
        /// Solution root directory where the solution and misc files will be written.
        /// </summary>
        AbsolutePath SolutionRoot { get; }

        /// <summary>
        /// Optional resharper dotsettings file to be placed next to the generated solution.
        /// </summary>
        AbsolutePath DotSettingsFile { get; }

        /// <summary>
        /// Optional list of target frameworks to which to restrict generated projects.
        /// </summary>
        IReadOnlyList<string> TargetFrameworks { get; }
    }
}
