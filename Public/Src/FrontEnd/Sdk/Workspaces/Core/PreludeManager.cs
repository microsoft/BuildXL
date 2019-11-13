// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Workspaces.Core
{
    /// <summary>
    /// Responsible for finding built-in prelude files and creating workspace-related artifacts for them.
    /// </summary>
    public sealed class PreludeManager : IPreludeManager
    {
        private const string PreludeDeploymentDirName = "Sdk.Prelude";
        private const string PreludeModuleConfigName = "package.config.dsc";
        private const string PreludeMainFile = "prelude.dsc";
        private const string PreludeOfficeBackCompatHacks = "Prelude.OfficeBackCompatHacks.dsc";

        private readonly FrontEndEngineAbstraction m_engine;
        private readonly PathTable m_pathTable;
        private readonly Func<AbsolutePath, Task<Possible<ISourceFile>>> m_parser;
        private readonly int m_maxParseConcurrency;
        private readonly bool m_useOfficeBackCompatHacks;

        private Possible<ParsedModule>? m_parsedPreludeModule = null;

        private PathTable PathTable => m_pathTable;

        /// <nodoc />
        public PreludeManager(FrontEndEngineAbstraction engine, PathTable pathTable, Func<AbsolutePath, Task<Possible<ISourceFile>>> parser, int maxParseConcurrency, bool useOfficeBackCompatHacks)
        {
            Contract.Requires(engine != null);
            Contract.Requires(pathTable.IsValid);
            Contract.Requires(parser != null);
            Contract.Requires(maxParseConcurrency > 0);

            m_engine = engine;
            m_pathTable = pathTable;
            m_parser = parser;
            m_maxParseConcurrency = maxParseConcurrency;
            m_useOfficeBackCompatHacks = useOfficeBackCompatHacks;
        }

        /// <summary>
        /// Creates or returns a previously created parsed prelude module.
        /// </summary>
        public async Task<Possible<ParsedModule>> GetOrCreateParsedPreludeModuleAsync()
        {
            if (m_parsedPreludeModule == null)
            {
                m_parsedPreludeModule = await CreatePreludeModuleAsync();
            } 

            return m_parsedPreludeModule.Value;
        }

        /// <summary>
        /// Returns a source resolver settings that point to the built-in prelude
        /// </summary>
        public static IResolverSettings GetResolverSettingsForBuiltInPrelude(AbsolutePath mainConfigurationFile, PathTable pathTable)
        {
            return new SourceResolverSettings {
                Modules = new[] { GetPathToPreludeModuleConfiguration(pathTable) },
                Kind = KnownResolverKind.DScriptResolverKind,
                Name = "BuiltInPrelude",
                Location = default(LineInfo),
                File = mainConfigurationFile
            };
        }

        /// <remarks>
        /// We have to construct a built-in prelude for configuration processing because 
        /// before we parse and convert the config file we don't know know where the user-specified
        /// prelude module resides.
        /// </remarks>
        [Pure]
        private async Task<Possible<ParsedModule>> CreatePreludeModuleAsync()
        {
            var moduleName = FrontEndHost.PreludeModuleName;

            string preludeRootDir = GetPreludeRoot();
            AbsolutePath preludeRootDirPath = AbsolutePath.Create(m_pathTable, preludeRootDir);

            if (!m_engine.DirectoryExists(preludeRootDirPath))
            {
                return new Failure<string>($"Prelude root folder '{preludeRootDir}' does not exist");
            }

            var specs = DiscoverPreludeSpecs(preludeRootDirPath, out AbsolutePath mainSpec, out AbsolutePath moduleConfigSpec);

            if (!mainSpec.IsValid)
            {
                return new Failure<string>($"Prelude main spec file ('{PreludeMainFile}') not found inside the '{preludeRootDir}' folder");
            }

            if (!moduleConfigSpec.IsValid)
            {
                return new Failure<string>($"Prelude module configuration spec file ('{PreludeModuleConfigName}') not found inside the '{preludeRootDir}' folder");
            }

            ModuleDefinition preludeModuleDefinition = ModuleDefinition.CreateModuleDefinitionWithExplicitReferencesWithEmptyQualifierSpace(
                descriptor: new ModuleDescriptor(
                    id: ModuleId.Create(m_pathTable.StringTable, moduleName), 
                    name: moduleName, 
                    displayName: moduleName,
                    version: "0", 
                    resolverKind: KnownResolverKind.DScriptResolverKind, 
                    resolverName:  "DScriptPrelude"),
                main: mainSpec,
                moduleConfigFile: moduleConfigSpec,
                specs: specs,
                pathTable: PathTable);

            var parsedPreludeFiles = await Task.WhenAll(specs.Select(m_parser));
            var failures = parsedPreludeFiles.Where(r => !r.Succeeded).ToList();
            if (failures.Count > 0)
            {
                var separator = Environment.NewLine + "  ";
                var message = "Parsing built-in prelude failed:" + separator + string.Join(separator, failures.Select(f => f.Failure.Describe()));
                return new Failure<string>(message);
            }

            Dictionary<AbsolutePath, ISourceFile> specFileMap = parsedPreludeFiles
                .ToDictionary(
                    maybeSourceFile => AbsolutePath.Create(PathTable, maybeSourceFile.Result.FileName),
                    maybeSourceFile => maybeSourceFile.Result);

            return new ParsedModule(preludeModuleDefinition, specFileMap);
        }

        private static string GetPreludeRoot()
        {
#if NET_FRAMEWORK
            var assemblyRootDir = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(typeof(PreludeManager).Assembly, computeAssemblyLocation: true));
#else
            // We won't recumpute the assembly location for now as we are running in the hosted .NET Core CLI process until we have self contained deployments;
            // Recomputing would mean the process path would be used to find assemblies, which would be the path where the CoreCLR CLI is located, thus wrong!
            var assemblyRootDir = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(typeof(PreludeManager).Assembly));
#endif
            if (BuildXL.Utilities.OperatingSystemHelper.IsUnixOS)
            {
                return Path.Combine(assemblyRootDir, PreludeDeploymentDirName);
            }

            return Path.Combine(assemblyRootDir, PreludeDeploymentDirName).ToLowerInvariant();
        }

        private IList<AbsolutePath> DiscoverPreludeSpecs(AbsolutePath preludeRootDir, out AbsolutePath mainSpec, out AbsolutePath moduleConfigSpec)
        {
            IList<AbsolutePath> preludeSpecs = m_engine
                .EnumerateFiles(preludeRootDir, "*.dsc", recursive: false)
                .Where(specPath => !string.Equals(specPath.GetName(m_pathTable).ToString(m_pathTable.StringTable), PreludeOfficeBackCompatHacks) || m_useOfficeBackCompatHacks)
                .ToList();

            mainSpec = preludeSpecs.FirstOrDefault(specPath => specPath.ToString(m_pathTable).EndsWith(PreludeMainFile, StringComparison.OrdinalIgnoreCase));
            moduleConfigSpec = preludeSpecs.FirstOrDefault(specPath => specPath.ToString(m_pathTable).EndsWith(PreludeModuleConfigName, StringComparison.OrdinalIgnoreCase));

            return preludeSpecs;
        }

        private static AbsolutePath GetPathToPreludeModuleConfiguration(PathTable pathTable)
        {
            var root = AbsolutePath.Create(pathTable, GetPreludeRoot());
            return root.Combine(pathTable, PreludeModuleConfigName);
        }
    }
}
