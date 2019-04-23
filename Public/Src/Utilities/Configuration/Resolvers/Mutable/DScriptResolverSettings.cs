// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public class SourceResolverSettings : DScriptResolverSettings
    {
        /// <nodoc />
        public SourceResolverSettings()
            : base()
        {
        }

        /// <nodoc />
        public SourceResolverSettings(IDScriptResolverSettings template, PathRemapper pathRemapper)
            : base(template, pathRemapper)
        {
        }
    }

    /// <nodoc />
    public class DScriptResolverSettings : ResolverSettings, IDScriptResolverSettings
    {
        /// <nodoc />
        public DScriptResolverSettings()
        {
            Modules = null; // Deliberate null, here as magic indication that none has been defined. All consumers are aware and deal with it.
            Packages = null; // Deliberate null, here as magic indication that none has been defined. All consumers are aware and deal with it.
        }

        /// <nodoc />
        public DScriptResolverSettings(IDScriptResolverSettings template, PathRemapper pathRemapper)
            : base(template, pathRemapper)
        {
            Contract.Assume(template != null);
            Contract.Assume(pathRemapper != null);

            Root = pathRemapper.Remap(template.Root);
            Modules = template.Modules?.Select(pathRemapper.Remap).ToList();
            Packages = template.Packages?.Select(pathRemapper.Remap).ToList();
        }

        /// <inheritdoc />
        public AbsolutePath Root { get; set; }

        /// <inheritdoc />
        public IReadOnlyList<AbsolutePath> Modules { get; set; }

        /// <inheritdoc />
        public IReadOnlyList<AbsolutePath> Packages { get; set; }
    }
}
