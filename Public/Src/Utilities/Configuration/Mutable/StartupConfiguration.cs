// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public sealed class StartupConfiguration : IStartupConfiguration
    {
        /// <nodoc />
        public StartupConfiguration()
        {
            AdditionalConfigFiles = new List<AbsolutePath>();
            Properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            QualifierIdentifiers = new List<string>();
            ImplicitFilters = new List<string>();

            CurrentHost = Host.Current;
        }

        /// <nodoc />
        public StartupConfiguration(IStartupConfiguration template, PathRemapper pathRemapper)
        {
            Contract.Assume(template != null);
            Contract.Assume(pathRemapper != null);

            ConfigFile = pathRemapper.Remap(template.ConfigFile);
            AdditionalConfigFiles = pathRemapper.Remap(template.AdditionalConfigFiles);
            Properties = new Dictionary<string, string>();
            foreach (var kv in template.Properties)
            {
                Properties.Add(kv.Key, kv.Value);
            }

            QualifierIdentifiers = new List<string>(template.QualifierIdentifiers);
            ImplicitFilters = new List<string>(template.ImplicitFilters);
            CurrentHost = new Host(template.CurrentHost, pathRemapper);
        }

        /// <inheritdoc />
        public AbsolutePath ConfigFile { get; set; }

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<AbsolutePath> AdditionalConfigFiles { get; set; }

        /// <inheritdoc />
        IReadOnlyList<AbsolutePath> IStartupConfiguration.AdditionalConfigFiles
        {
            get { return AdditionalConfigFiles; }
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public Dictionary<string, string> Properties { get; set; }

        /// <inheritdoc />
        IReadOnlyDictionary<string, string> IStartupConfiguration.Properties => Properties;

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<string> QualifierIdentifiers { get; set; }

        /// <inheritdoc />
        IReadOnlyList<string> IStartupConfiguration.QualifierIdentifiers => QualifierIdentifiers;

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<string> ImplicitFilters { get; set; }

        /// <inheritdoc />
        IReadOnlyList<string> IStartupConfiguration.ImplicitFilters => ImplicitFilters;

        /// <inheritdoc />
        public IHost CurrentHost { get; set; }

        /// <summary>
        /// Ensures the properties are set up properly when running in CloudBuild.
        /// </summary>
        public void EnsurePropertiesWhenRunInCloudBuild()
        {
            Properties[BuildParameters.IsInCloudBuildVariableName] = "1";
        }
    }
}
