// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
            CanWriteToSrc = template.CanWriteToSrc;
            SolutionName = pathRemapper.Remap(template.SolutionName);
            SolutionRoot = pathRemapper.Remap(template.SolutionRoot);
            DotSettingsFile = pathRemapper.Remap(template.DotSettingsFile);
            TargetFrameworks = new List<string>(template.TargetFrameworks);
        }

        /// <inheritdoc />
        public bool IsEnabled { get; set; }

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
            get { return SolutionRoot; }
            set { SolutionRoot = value; }
        }

        /// <inheritdoc />
        public AbsolutePath DotSettingsFile { get; set; }

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<string> TargetFrameworks { get; set; } = new List<string>();

        /// <inheritdoc />
        IReadOnlyList<string> IIdeConfiguration.TargetFrameworks => TargetFrameworks;
    }
}
