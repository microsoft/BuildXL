// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public sealed class IdeConfiguration : IIdeConfiguration
    {
        /// <nodoc />
        public IdeConfiguration()
        {
        }

        /// <nodoc />
        public IdeConfiguration(IIdeConfiguration template, PathRemapper pathRemapper)
        {
            Contract.Assume(template != null);

            IsEnabled = template.IsEnabled;
            IsNewEnabled = template.IsNewEnabled;
            CanWriteToSrc = template.CanWriteToSrc;
            SolutionName = pathRemapper.Remap(template.SolutionName);
            SolutionRoot = pathRemapper.Remap(template.SolutionRoot);
            DotSettingsFile = pathRemapper.Remap(template.DotSettingsFile);
        }

        /// <inheritdoc />
        public bool IsEnabled { get; set; }

        /// <inheritdoc />
        public bool IsNewEnabled { get; set; }

        /// <inheritdoc />
        public PathAtom SolutionName { get; set; }

        /// <inheritdoc />
        public bool? CanWriteToSrc { get; set; }

        /// <inheritdoc />
        public AbsolutePath SolutionRoot { get; set; }

        /// <nodoc />
        // Temporary redirect for back compat
        [Obsolete]
        public AbsolutePath VsDominoRoot { 
            get { return SolutionRoot;} 
            set { SolutionRoot = value; }
        }

        /// <inheritdoc />
        public AbsolutePath DotSettingsFile { get; set; }
    }
}
