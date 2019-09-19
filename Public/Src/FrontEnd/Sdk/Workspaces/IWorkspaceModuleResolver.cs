// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using JetBrains.Annotations;
using TypeScript.Net.DScript;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Workspaces
{
    /// <summary>
    /// A workspace resolver has knowledge of a set of module definitions and supports a number of queries across them.
    /// </summary>
    public interface IWorkspaceModuleResolver
    {
        /// <summary>
        /// Initializes the workspace resolver
        /// </summary>
        bool TryInitialize(
            [JetBrains.Annotations.NotNull]FrontEndHost host,
            [JetBrains.Annotations.NotNull]FrontEndContext context,
            [JetBrains.Annotations.NotNull]IConfiguration configuration,
            [JetBrains.Annotations.NotNull]IResolverSettings resolverSettings,
            [JetBrains.Annotations.NotNull]QualifierId[] requestedQualifiers);

        /// <summary>
        /// If <param name="moduleDescriptor"/> is owned by this resolver, returns the ModuleDefinition with that name.
        /// </summary>
        ValueTask<Possible<ModuleDefinition>> TryGetModuleDefinitionAsync(ModuleDescriptor moduleDescriptor);

        /// <summary>
        /// Returns the set of module descriptors owned by this resolver with name <param name="moduleReference"/>. Consider there may be more than one, when versions are present.
        /// </summary>
        ValueTask<Possible<IReadOnlyCollection<ModuleDescriptor>>> TryGetModuleDescriptorsAsync(ModuleReferenceWithProvenance moduleReference);

        /// <summary>
        /// Returns the module descriptor that contains <param name="specPath"/> if such module is known to this resolver.
        /// </summary>
        ValueTask<Possible<ModuleDescriptor>> TryGetOwningModuleDescriptorAsync(AbsolutePath specPath);

        /// <summary>
        /// Returns all the module descriptors owned by this resolver.
        /// </summary>
        /// <remarks>
        /// The result may only include modules that are statically known based on a particular
        /// configuration of this resolver.
        /// </remarks>
        ValueTask<Possible<HashSet<ModuleDescriptor>>> GetAllKnownModuleDescriptorsAsync();

        /// <summary>
        /// Returns a parsed spec that <param name="pathToParse"/> points to when prompted in <param name="moduleOrConfigPathPromptingParse" />.
        /// </summary>
        /// <remarks>
        /// DScript-specific parsing options can be passed with <param name="parsingOptions"/>
        /// </remarks>
        Task<Possible<ISourceFile>> TryParseAsync(AbsolutePath pathToParse, AbsolutePath moduleOrConfigPathPromptingParse, ParsingOptions parsingOptions = null);

        /// <summary>
        /// Returns a user-facing description of the resolver extent (e.g. what modules the resolver owns, or what directories are looked up)
        /// </summary>
        [JetBrains.Annotations.NotNull]
        string DescribeExtent();

        /// <summary>
        /// Returns a symbolic name of the kind of resolver that this resolver instance is.
        /// </summary>
        string Kind { get; }

        /// <summary>
        /// Returns a unique name for each instantiated resolver.
        /// </summary>
        /// <remarks>
        /// This can be a user provided name or it is automatically filled in by the FrontEndController based on the Kind name.
        /// </remarks>
        string Name { get; }

        /// <summary>
        /// In some IDE scenarios (like adding a new file) resolver initialization is required.
        /// </summary>
        Task ReinitializeResolver();

        /// <summary>
        /// Returns all module configuration files parsed during module resolution.
        /// </summary>
        /// <remarks>
        /// To avoid memory leaks, the implementation should keep the module configuration files around only until the first time this method is called;
        /// as soon as this method is called, any internal reference to these files(e.g., stored in a field) should be released
        /// TODO: this restriction (or design in general) should be changed to avoid such a complicated and fragile restrictions.
        /// </remarks>
        ISourceFile[] GetAllModuleConfigurationFiles();
    }

    /// <nodoc/>
    public static class WorkspaceResolverExtensionMethods
    {
        /// <summary>
        /// Returns all the module definitions owned by this resolver.
        /// </summary>
        public static async Task<Possible<IReadOnlyCollection<ModuleDefinition>>> GetAllModuleDefinitionsAsync(this IWorkspaceModuleResolver moduleResolver)
        {
            var names = await moduleResolver.GetAllKnownModuleDescriptorsAsync();

            if (!names.Succeeded)
            {
                return names.Failure;
            }

            // We bail out on first failure
            var moduleDefinitions = new List<ModuleDefinition>(names.Result.Count);
            foreach (var name in names.Result)
            {
                var maybeModuleDefinition = await moduleResolver.TryGetModuleDefinitionAsync(name);
                if (!maybeModuleDefinition.Succeeded)
                {
                    return maybeModuleDefinition.Failure;
                }

                moduleDefinitions.Add(maybeModuleDefinition.Result);
            }

            return moduleDefinitions;
        }

        /// <summary>
        /// Returns the module definition that contains <param name="specPath"/> if such a module is known
        /// to <param name="moduleResolver"/>
        /// </summary>
        public static async ValueTask<Possible<ModuleDefinition>> TryGetOwningModuleDefinitionAsync(
            this IWorkspaceModuleResolver moduleResolver, AbsolutePath specPath)
        {
            var maybeName = await moduleResolver.TryGetOwningModuleDescriptorAsync(specPath);

            if (maybeName.Succeeded)
            {
                return await moduleResolver.TryGetModuleDefinitionAsync(maybeName.Result);
            }

            return maybeName.Failure;
        }
    }
}
