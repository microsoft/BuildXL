// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <summary>
    /// Resolver settings for the Lage front-end.
    /// </summary>
    public class LageResolverSettings : JavaScriptResolverSettings, ILageResolverSettings
    {
        /// <nodoc/>
        public LageResolverSettings()
        {
            Targets = new List<string>();
        }

        /// <nodoc/>
        public LageResolverSettings(ILageResolverSettings template, PathRemapper pathRemapper) 
        : base(template, pathRemapper)
        {
            Targets = new List<string>(template.Targets.Count);
            foreach (var target in template.Targets)
            {
                Targets.Add(target);
            }
        }
    
        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<string> Targets { get; set; }

        /// <inheritdoc />
        IReadOnlyList<string> ILageResolverSettings.Targets => Targets;

    }
}
