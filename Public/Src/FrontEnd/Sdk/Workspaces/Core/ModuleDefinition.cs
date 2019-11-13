// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using JetBrains.Annotations;

namespace BuildXL.FrontEnd.Workspaces.Core
{
    /// <summary>
    /// The description of a module, including the set of (paths to) specs that the module definition contains.
    /// </summary>
    [DebuggerDisplay("ModuleDefinition = {Descriptor.Name}")]
    public sealed class ModuleDefinition : IEquatable<ModuleDefinition>
    {
        // This should be always in sync with BuildXL.FrontEnd.Sdk.Qualifier.QualifierSpaceId.Invalid.
        // All qualifier-related types live in a separate dll we don't want to reference since it is BuildXL-engine related.
        // A refactoring would be needed to expose qualifiers from a common place, but since this is a V1 specific field
        // that should go away we just expose them as integers
        private const int InvalidQualifierSpaceId = -1;

        // Only used for testing
        private const int EmptyQualifierSpaceId = 0;

        /// <summary>
        /// Qualifier space id for the module
        /// </summary>
        /// <remarks>
        /// This is a V1 specific field. TODO: remove when DScript V2 becomes the norm
        /// </remarks>
        public int V1QualifierSpaceId { get; }

        /// <nodoc/>
        public ModuleDescriptor Descriptor { get; }

        /// <summary>
        /// Path to main module entry point file (can be invalid).
        /// </summary>
        /// <remarks>
        /// a) Observe that when the main file is valid, it is always right under <see cref="Root"/>.
        /// b) This is specific to modules with explicit references, and it is invalid otherwise
        ///
        /// TODO: remove when DScript V2 becomes the norm
        /// </remarks>
        public AbsolutePath MainFile { get; }

        /// <summary>
        /// Path to the root directory of the package
        /// </summary>
        public AbsolutePath Root { get; }

        /// <summary>
        /// Path to the config file that defined this module.
        /// </summary>
        public AbsolutePath ModuleConfigFile { get; }

        /// <summary>
        /// List of specs that constitute this module
        /// </summary>
        public IReadOnlySet<AbsolutePath> Specs { get; }

        /// <nodoc/>
        public NameResolutionSemantics ResolutionSemantics { get; }

        /// <nodoc/>
        [CanBeNull]
        public IEnumerable<ModuleReferenceWithProvenance> AllowedModuleDependencies { get; }

        /// <nodoc/>
        [CanBeNull]
        public IEnumerable<ModuleReferenceWithProvenance> CyclicalFriendModules { get; }

        /// <nodoc/>
        public ModuleDefinition(
            ModuleDescriptor descriptor,
            AbsolutePath root,
            AbsolutePath mainFile,
            AbsolutePath moduleConfigFile,
            IEnumerable<AbsolutePath> specs,
            NameResolutionSemantics resolutionSemantics,
            int v1QualifierSpaceId,
            [CanBeNull] IEnumerable<ModuleReferenceWithProvenance> allowedModuleDependencies,
            [CanBeNull] IEnumerable<ModuleReferenceWithProvenance> cyclicalFriendModules)
        {
            Contract.Requires(specs != null);
            Contract.Requires(root.IsValid);

            AllowedModuleDependencies = allowedModuleDependencies;
            CyclicalFriendModules = cyclicalFriendModules;

            // Main file is valid iff the resolution semantics is explicit references
            Contract.Requires(resolutionSemantics == NameResolutionSemantics.ImplicitProjectReferences
                ? !mainFile.IsValid
                : mainFile.IsValid);

            Descriptor = descriptor;
            MainFile = mainFile;
            Root = root;
            ModuleConfigFile = moduleConfigFile;

            // Specs could have duplicates, and the following line removes them.
            Specs = specs.ToReadOnlySet();
            ResolutionSemantics = resolutionSemantics;
            V1QualifierSpaceId = v1QualifierSpaceId;
        }

        /// <summary>
        /// Creates a module definition with implicit reference semantics
        /// </summary>
        public static ModuleDefinition CreateModuleDefinitionWithImplicitReferences(
            ModuleDescriptor descriptor,
            AbsolutePath moduleRootDirectory,
            AbsolutePath moduleConfigFile,
            IEnumerable<AbsolutePath> specs,
            [CanBeNull] IEnumerable<ModuleReferenceWithProvenance> allowedModuleDependencies,
            [CanBeNull] IEnumerable<ModuleReferenceWithProvenance> cyclicalFriendModules)
        {
            return new ModuleDefinition(
                descriptor,
                moduleRootDirectory,
                AbsolutePath.Invalid,
                moduleConfigFile,
                specs,
                NameResolutionSemantics.ImplicitProjectReferences,
                InvalidQualifierSpaceId,
                allowedModuleDependencies,
                cyclicalFriendModules);
        }

        /// <summary>
        /// Creates a module definition with explicit reference semantics
        /// </summary>
        /// <remarks>
        /// In this case a main file has to be provided. The root of the package is computed from it.
        /// </remarks>
        public static ModuleDefinition CreateModuleDefinitionWithExplicitReferences(
            ModuleDescriptor descriptor,
            AbsolutePath main,
            AbsolutePath moduleConfigFile,
            IEnumerable<AbsolutePath> specs,
            PathTable pathTable,
            int qualifierSpaceId)
        {
            return new ModuleDefinition(
                descriptor,
                main.GetParent(pathTable),
                main,
                moduleConfigFile,
                specs,
                NameResolutionSemantics.ExplicitProjectReferences,
                qualifierSpaceId,
                allowedModuleDependencies: null,
                cyclicalFriendModules: null);
        }

        /// <summary>
        /// Creates a module definition for configuration file.
        /// </summary>
        public static ModuleDefinition CreateConfigModuleDefinition(PathTable pathTable, AbsolutePath configPath, IEnumerable<AbsolutePath> allSpecs)
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(configPath.IsValid);
            Contract.Requires(allSpecs != null);

            var descriptorName = Names.ConfigModuleName;
            var mdsc = new ModuleDescriptor(
                id: ModuleId.Create(pathTable.StringTable, descriptorName),
                name: descriptorName,
                displayName: descriptorName,
                version: "0.0",
                resolverKind: KnownResolverKind.DScriptResolverKind,
                resolverName: "DScriptConfiguration");
            return ModuleDefinition.CreateModuleDefinitionWithExplicitReferencesWithEmptyQualifierSpace(
                descriptor: mdsc,
                main: configPath,
                moduleConfigFile: AbsolutePath.Invalid,
                specs: allSpecs,
                pathTable: pathTable);
        }

        /// <summary>
        /// Creates a module definition with explicit reference semantics and an empty qualifier space id
        /// </summary>
        /// <remarks>
        /// This is only used mainly for testing.
        /// </remarks>
        public static ModuleDefinition CreateModuleDefinitionWithExplicitReferencesWithEmptyQualifierSpace(
            ModuleDescriptor descriptor,
            AbsolutePath main,
            AbsolutePath moduleConfigFile,
            IEnumerable<AbsolutePath> specs,
            PathTable pathTable)
        {
            return new ModuleDefinition(
                descriptor,
                main.GetParent(pathTable),
                main,
                moduleConfigFile,
                specs,
                NameResolutionSemantics.ExplicitProjectReferences,
                EmptyQualifierSpaceId,
                allowedModuleDependencies: null,
                cyclicalFriendModules: null);
        }

        /// <summary>
        /// Creates new instance with a given specs.
        /// </summary>
        public ModuleDefinition WithSpecs(IReadOnlySet<AbsolutePath> specs)
        {
            Contract.Requires(specs != null);
            return new ModuleDefinition(
                Descriptor,
                Root,
                MainFile,
                ModuleConfigFile,
                specs,
                ResolutionSemantics,
                V1QualifierSpaceId,
                AllowedModuleDependencies,
                CyclicalFriendModules);
        }

        /// <inheritdoc />
        public bool Equals(ModuleDefinition other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return Descriptor.Equals(other.Descriptor);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            var rhs = obj as ModuleDefinition;
            return rhs != null && Equals(rhs);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Descriptor.GetHashCode();
        }
    }
}
