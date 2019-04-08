// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public sealed class InternalDefaultDScriptResolverSettings : SourceResolverSettings, IInternalDefaultDScriptResolverSettings
    {
        /// <nodoc />
        public InternalDefaultDScriptResolverSettings()
        {
            Projects = null; // Deliberate null, here as magic indication that none has been defined. All consumers are aware and deal with it.
        }

        /// <nodoc />
        public InternalDefaultDScriptResolverSettings(IInternalDefaultDScriptResolverSettings template, PathRemapper pathRemapper)
            : base(template, pathRemapper)
        {
            Contract.Assume(template != null);
            Contract.Assume(pathRemapper != null);

            Projects = template.Projects == null ? null : new List<AbsolutePath>(template.Projects);
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<AbsolutePath> Projects { get; set; }

        /// <inheritdoc />
        IReadOnlyList<AbsolutePath> IInternalDefaultDScriptResolverSettings.Projects => Projects;

        /// <summary>
        /// Path to the configuration file.
        /// </summary>
        public AbsolutePath ConfigFile { get; set; }
    }
}
