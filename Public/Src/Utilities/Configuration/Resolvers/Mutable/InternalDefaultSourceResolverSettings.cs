// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;

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
