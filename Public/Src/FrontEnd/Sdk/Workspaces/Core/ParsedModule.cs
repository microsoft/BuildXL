// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.FrontEnd.Sdk.Workspaces;
using TypeScript.Net.Types;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Workspaces.Core
{
    /// <summary>
    /// Represents a set of parsed specs (plus its module definition).
    /// </summary>
    [DebuggerDisplay("ParsedModule = {Descriptor.Name}")]
    public sealed class ParsedModule
    {
        /// <nodoc/>
        public IReadOnlyDictionary<AbsolutePath, ISourceFile> Specs { get; }

        /// <nodoc/>
        public ModuleDefinition Definition { get; }

        /// <nodoc/>
        public ModuleDescriptor Descriptor => Definition.Descriptor;

        /// <nodoc/>
        public IReadOnlyCollection<AbsolutePath> PathToSpecs => Definition.Specs;

        /// <nodoc/>
        public IReadOnlySet<(ModuleDescriptor moduleDescriptor, Location location)> ReferencedModules { get; }

        /// <nodoc/>
        public ParsedModule(ModuleDefinition moduleDefinition, bool hasFailures = false)
            : this(moduleDefinition, new Dictionary<AbsolutePath, ISourceFile>(), CollectionUtilities.EmptySet<(ModuleDescriptor, Location)>(), hasFailures)
        { }

        /// <nodoc/>
        public ParsedModule(ModuleDefinition moduleDefinition, IReadOnlyDictionary<AbsolutePath, ISourceFile> specs)
            : this(moduleDefinition, specs, CollectionUtilities.EmptySet<(ModuleDescriptor, Location)>())
        {
        }

        /// <summary>
        /// For every specs contained in <param name="specs"/>, there should be an equivalent (path to) spec
        /// in <param name="moduleDefinition"/>. <param name="referencedModules"/> contains the modules actually referenced
        /// by all specs in this parsed module.
        /// </summary>
        /// <remarks>
        /// If <param name="hasFailures"/> is false, then the number of parsed specs (<paramref name="specs"/> should match exactly the number of specs in a <paramref name="moduleDefinition"/>.
        /// </remarks>
        public ParsedModule(ModuleDefinition moduleDefinition, IReadOnlyDictionary<AbsolutePath, ISourceFile> specs, IReadOnlySet<(ModuleDescriptor, Location)> referencedModules, bool hasFailures = false)
        {
            Contract.Requires(moduleDefinition != null);
            Contract.Requires(specs != null);
            Contract.Requires(referencedModules != null);

            if (!hasFailures && moduleDefinition.Specs.Count != specs.Count)
            {
                // The check should happen only if no errors happened.
                throw Contract.AssertFailure(I($"Parsed module should have exact number of parsed specs as module definition. #specs in module definition is '{moduleDefinition.Specs.Count}', #parsed specs is '{specs.Count}'"));
            }

            Specs = specs;
            Definition = moduleDefinition;

            ReferencedModules = referencedModules;
        }
    }
}
