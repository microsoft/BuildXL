// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
        /// Temporary option for enabling "new" vs solution generator
        /// </summary>
        bool IsNewEnabled { get; }

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
    }
}
