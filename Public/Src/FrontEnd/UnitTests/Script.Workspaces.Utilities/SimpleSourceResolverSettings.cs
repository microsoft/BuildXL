// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.FrontEnd.Sdk.FileSystem;

namespace Test.DScript.Workspaces.Utilities
{
    /// <summary>
    /// A source resolver that is defined directly passing the module definitions that will be part of
    /// the corresponding <see cref="SimpleWorkspaceSourceModuleResolver"/> and the <see cref="IFileSystem"/> the resolver is going to see.
    /// </summary>
    public sealed class SimpleSourceResolverSettings : IResolverSettings
    {
        /// <nodoc/>
        public Dictionary<ModuleDescriptor, ModuleDefinition> ModuleDefinitions { get; }

        /// <nodoc/>
        public IFileSystem FileSystem { get; }

        /// <nodoc/>
        public string Kind { get; }

        /// <nodoc/>
        public string Name { get; private set; }

        /// <nodoc/>
        public AbsolutePath File { get; }

        /// <nodoc/>
        public bool AllowWritableSourceDirectory { get; }

        /// <nodoc/>
        public LineInfo Location { get; }

        /// <nodoc/>
        public SimpleSourceResolverSettings(
            Dictionary<ModuleDescriptor, ModuleDefinition> moduleDefinitions,
            IFileSystem fileSystem,
            AbsolutePath file = default(AbsolutePath),
            LineInfo location = default(LineInfo))
        {
            Kind = KnownResolverKind.DScriptResolverKind;
            Name = nameof(SimpleSourceResolverSettings);
            ModuleDefinitions = moduleDefinitions;
            FileSystem = fileSystem;
            File = file;
            Location = location;
            AllowWritableSourceDirectory = false;
        }

        /// <inheritdoc />
        public void SetName(string name)
        {
            Contract.Requires(string.IsNullOrEmpty(name), "Expected name to only be set once if not set by default");
            Contract.Requires(!string.IsNullOrEmpty(name));
            Name = name;
        }
    }
}
