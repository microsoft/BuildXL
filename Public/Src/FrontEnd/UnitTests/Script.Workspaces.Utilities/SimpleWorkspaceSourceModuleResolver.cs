// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.FileSystem;
using BuildXL.FrontEnd.Workspaces;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Tasks;
using JetBrains.Annotations;
using TypeScript.Net.DScript;
using TypeScript.Net.Parsing;
using TypeScript.Net.Types;

namespace Test.DScript.Workspaces.Utilities
{
    /// <summary>
    /// A source resolver that is configured with a <see cref="SimpleSourceResolverSettings"/>
    /// </summary>
    public sealed class SimpleWorkspaceSourceModuleResolver : IWorkspaceModuleResolver
    {
        private Dictionary<ModuleDescriptor, ModuleDefinition> m_moduleDefinitions;

        private IFileSystem m_fileSystem;

        /// <inheritdoc />
        public string Kind => KnownResolverKind.DScriptResolverKind;

        /// <inheritdoc />
        public string Name { get; private set; }

        /// <inheritdoc />
        public bool TryInitialize(
            [NotNull] FrontEndHost host,
            [NotNull] FrontEndContext context,
            [NotNull] IConfiguration configuration,
            [NotNull] IResolverSettings resolverSettings)
        {
            var sourceResolverSettings = resolverSettings as SimpleSourceResolverSettings;

            Contract.Assert(sourceResolverSettings != null);
            Name = resolverSettings.Name;
            m_moduleDefinitions = sourceResolverSettings.ModuleDefinitions;
            m_fileSystem = sourceResolverSettings.FileSystem;

            return true;
        }


        /// <nodoc/>
        public ValueTask<Possible<ModuleDefinition>> TryGetModuleDefinitionAsync(ModuleDescriptor moduleDescriptor)
        {
            return ValueTaskFactory.FromResult(m_moduleDefinitions.ContainsKey(moduleDescriptor)
                ? new Possible<ModuleDefinition>(m_moduleDefinitions[moduleDescriptor])
                : new Possible<ModuleDefinition>(
                    new ModuleNotFoundInSimpleSourceResolverFailure(moduleDescriptor)));
        }

        /// <nodoc/>
        public ValueTask<Possible<IReadOnlyCollection<ModuleDescriptor>>> TryGetModuleDescriptorsAsync(ModuleReferenceWithProvenance moduleReference)
        {
            return
                ValueTaskFactory.FromResult(
                    new Possible<IReadOnlyCollection<ModuleDescriptor>>(
                        m_moduleDefinitions.Keys.Where(moduleDescriptor => moduleDescriptor.Name.Equals(moduleReference.Name))
                            .ToList()));
        }

        /// <nodoc/>
        public ValueTask<Possible<ModuleDescriptor>> TryGetOwningModuleDescriptorAsync(AbsolutePath specPath)
        {
            foreach (var moduleDefinition in m_moduleDefinitions.Values)
            {
                if (moduleDefinition.Specs.Contains(specPath))
                {
                    return ValueTaskFactory.FromResult(new Possible<ModuleDescriptor>(moduleDefinition.Descriptor));
                }
            }

            return
                ValueTaskFactory.FromResult(
                    new Possible<ModuleDescriptor>(new ModuleNotFoundInSimpleSourceResolverFailure(specPath)));
        }

        /// <nodoc/>
        public ValueTask<Possible<HashSet<ModuleDescriptor>>> GetAllKnownModuleDescriptorsAsync()
        {
            return
                ValueTaskFactory.FromResult(
                    new Possible<HashSet<ModuleDescriptor>>(new HashSet<ModuleDescriptor>(m_moduleDefinitions.Keys)));
        }

        /// <nodoc/>
        public async Task<Possible<ISourceFile>> TryParseAsync(AbsolutePath pathToParse, AbsolutePath moduleOrConfigPathPromptingParse, ParsingOptions parsingOptions = null)
        {
            Contract.Requires(pathToParse.IsValid);

            // Check if the file exists and that it is not a directory
            if (!m_fileSystem.Exists(pathToParse))
            {
                return new CannotReadSpecFailure(pathToParse.ToString(m_fileSystem.GetPathTable()), CannotReadSpecFailure.CannotReadSpecReason.SpecDoesNotExist);
            }

            if (m_fileSystem.IsDirectory(pathToParse))
            {
                return new CannotReadSpecFailure(pathToParse.ToString(m_fileSystem.GetPathTable()), CannotReadSpecFailure.CannotReadSpecReason.PathIsADirectory);
            }

           // Parses the spec
            ISourceFile parsedFile;
            using (var reader = m_fileSystem.OpenText(pathToParse))
            {
                var content = await reader.ReadToEndAsync();
                var parser = new Parser();

                parsedFile = parser.ParseSourceFileContent(m_fileSystem.GetBaseName(pathToParse), content, parsingOptions);
            }

            return new Possible<ISourceFile>(parsedFile);
        }

         /// <nodoc/>
        public string DescribeExtent()
        {
            return string.Format(CultureInfo.InvariantCulture, "[SimpleSourceWorkspaceResolver] Known modules: {0}",
                GetModuleDefinitionNamesString());
        }

        /// <nodoc />
        public Task ReinitializeResolver() => Task.FromResult<object>(null);

        /// <inheritdoc />
        public ISourceFile[] GetAllModuleConfigurationFiles()
        {
            return CollectionUtilities.EmptyArray<ISourceFile>();
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return DescribeExtent();
        }

        private string GetModuleDefinitionNamesString()
        {
            using (var builder = Pools.GetStringBuilder())
            {
                var sb = builder.Instance;

                foreach (var module in m_moduleDefinitions)
                {
                    sb.Append(string.Format(CultureInfo.InvariantCulture, "'{0}', ", module.Key));
                }

                return sb.ToString().TrimEnd(',', ' ');
            }
        }
    }

    /// <summary>
    /// A given module was not found
    /// </summary>
    internal sealed class ModuleNotFoundInSimpleSourceResolverFailure : WorkspaceFailure
    {
        private readonly AbsolutePath m_pathToSpec;
        private readonly ModuleDescriptor m_moduleDescriptor;

        /// <nodoc/>
        public ModuleNotFoundInSimpleSourceResolverFailure(ModuleDescriptor moduleDescriptor)
        {
            m_moduleDescriptor = moduleDescriptor;
        }

        /// <nodoc/>
        public ModuleNotFoundInSimpleSourceResolverFailure(AbsolutePath pathToSpec)
        {
            Contract.Requires(pathToSpec.IsValid);

            m_pathToSpec = pathToSpec;
        }

        /// <inheritdoc/>
        public override string Describe()
        {
            if (!m_pathToSpec.IsValid)
            {
                return string.Format(CultureInfo.InvariantCulture, "Module '{0}' was not found.", m_moduleDescriptor);
            }

            return string.Format(CultureInfo.InvariantCulture, "Spec '{0}' was not found.", m_pathToSpec);
        }
    }
}
