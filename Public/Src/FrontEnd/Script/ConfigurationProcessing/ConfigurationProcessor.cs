// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.FrontEnd.Script.Constants;
using JetBrains.Annotations;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.FrontEnd.Core;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Util;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.Workspaces;
using Logger = BuildXL.FrontEnd.Script.Tracing.Logger;

namespace BuildXL.FrontEnd.Script
{
    /// <summary>
    /// Special version of a DScript front-end responsible for configuration parsing.
    /// </summary>
    /// <remarks>
    /// Due to current design issues, current type still needs to implement <see cref="IResolver"/> interface.
    /// But all methods from it will throw.
    /// </remarks>
    public class ConfigurationProcessor : DScriptInterpreterBase, IConfigurationProcessor
    {
        /// <summary>
        /// Returns a workspace for the main configuration file.
        /// </summary>
        /// <remarks>
        /// Not null if the configuration is processed successfully.
        /// </remarks>
        [CanBeNull]
        public IWorkspace PrimaryConfigurationWorkspace { get; private set; }

        /// <inheritdoc />
        public IConfigurationStatistics GetConfigurationStatistics() => FrontEndStatistics.LoadConfigStatistics;

        /// <nodoc />
        public ConfigurationProcessor(
            GlobalConstants constants,
            ModuleRegistry sharedModuleRegistry,
            IFrontEndStatistics statistics,
            Logger logger)
            : base(constants, sharedModuleRegistry, statistics, logger)
        {
        }

        /// <inheritdoc />
        AbsolutePath IConfigurationProcessor.FindPrimaryConfiguration(IStartupConfiguration startupConfiguration)
        {
            if (startupConfiguration.ConfigFile.IsValid)
            {
                return startupConfiguration.ConfigFile;
            }

            var startDirectory = AbsolutePath.Create(Context.PathTable, Directory.GetCurrentDirectory());
            return TryFindConfig(startDirectory);
        }

        /// <inheritdoc />
        IConfiguration IConfigurationProcessor.InterpretConfiguration(
            AbsolutePath primaryConfigurationFile,
            ICommandLineConfiguration commandLineConfiguration)
        {
            Contract.Requires(primaryConfigurationFile.IsValid);
            Contract.Requires(commandLineConfiguration != null);

            var configObjectLiteral = ParseAndInterpretConfigFile(primaryConfigurationFile);
            if (configObjectLiteral == null)
            {
                // Error has been reported already
                return null;
            }

            // Apply Additional configurations from the commandline
            foreach (var additionalConfigurationFile in commandLineConfiguration.Startup.AdditionalConfigFiles)
            {
                configObjectLiteral = ParseAndInterpretConfigFile(additionalConfigurationFile, configObjectLiteral);
            }

            // TODO: user override is not really working now. Fix me!

            try
            {
                // Merge the object literal with the initial C# defaults.
                return ConfigurationConverter.AugmentConfigurationWith(Context, commandLineConfiguration, configObjectLiteral);
            }
            catch (ConversionException conversionException)
            {
                var configFileString = primaryConfigurationFile.ToString(Context.PathTable);
                Logger.ReportConversionException(
                    Context.LoggingContext,
                    new Location() { File = configFileString },
                    Name,
                    GetConversionExceptionMessage(primaryConfigurationFile, conversionException));
                return null;
            }
        }

        /// <inheritdoc />
        void IConfigurationProcessor.Initialize(FrontEndHost host, FrontEndContext context)
        {
            Contract.Requires(host != null);
            Contract.Requires(context != null);

            InitializeInterpreter(host, context, new ConfigurationImpl());
        }

        private AbsolutePath TryFindConfig(AbsolutePath startDir)
        {
            Contract.Requires(startDir.IsValid);

            var dirInfo = new DirectoryInfo(startDir.ToString(Context.PathTable));

            if (!Directory.Exists(dirInfo.FullName))
            {
                return AbsolutePath.Invalid;
            }

            var walk = dirInfo;

            for (; walk != null; walk = walk.Parent)
            {
                var configFilename = Path.Combine(walk.FullName, Script.Constants.Names.ConfigBc);
                var legacyConfigFilename = Path.Combine(walk.FullName, Script.Constants.Names.ConfigDsc);

                var legacyConfigFile = AbsolutePath.Create(Context.PathTable, legacyConfigFilename.Replace('\\', '/'));
                var configFile = AbsolutePath.Create(Context.PathTable, configFilename.Replace('\\', '/'));
                if (m_fileSystem.Exists(legacyConfigFile))
                {
                    return legacyConfigFile;
                }
                if (m_fileSystem.Exists(configFile))
                {
                    return configFile;
                }
            }

            return AbsolutePath.Invalid;
        }


        private ObjectLiteral ParseAndInterpretConfigFile(AbsolutePath additionalConfigPath, ObjectLiteral configObjectLiteral = null)
        {
            return ParseAndInterpretConfigFileAsync(additionalConfigPath, configObjectLiteral).GetAwaiter().GetResult();
        }

        private async Task<ObjectLiteral> ParseAndInterpretConfigFileAsync(AbsolutePath configPath, ObjectLiteral configObjectLiteralMerging)
        {
            Contract.Requires(configPath.IsValid);

            // must create a helper NOW (instead of in 'Initialize') because the value of 'Engine' now might be different from the value in 'Initialize'
            var configHelper = CreateHelper();
            var parsedConfig = await configHelper.ParseValidateAndConvertConfigFileAsync(configPath);
            if (!parsedConfig.Success)
            {
                // Errors should have been reported during parsing.
                return null;
            }

            var configObjectLiteral = EvaluateConfigObjectLiteral(parsedConfig.Result, configObjectLiteralMerging);
            if (configObjectLiteral == null)
            {
                var configPathString = configPath.ToString(Context.PathTable);
                Logger.ReportSourceResolverConfigurationIsNotObjectLiteral(
                    Context.LoggingContext,
                    new Location() { File = configPathString },
                    frontEndName: null,
                    configPath: configPathString);
                return null;
            }

            // Re-create workspace without typechecking for the purpose of storing it in PrimaryConfigurationWorkspace
            var nonTypeCheckedWorkspace = await configHelper.ParseAndValidateConfigFileAsync(configPath, typecheck: false);
            Contract.Assert(nonTypeCheckedWorkspace != null && nonTypeCheckedWorkspace.Succeeded);
            PrimaryConfigurationWorkspace = nonTypeCheckedWorkspace;

            return configObjectLiteral;
        }

        private ConfigurationConversionHelper CreateHelper()
        {
            return new ConfigurationConversionHelper(
                Engine,
                ConfigurationConversionHelper.ConfigurationKind.PrimaryConfig,
                Constants,
                SharedModuleRegistry,
                Logger,
                FrontEndHost,
                Context,
                Configuration,
                FrontEndStatistics);
        }

        private ObjectLiteral EvaluateConfigObjectLiteral(FileModuleLiteral moduleLiteral, ObjectLiteral configObjectLiteral)
        {
            // Instantiate config module, and because config is qualifier-agnositic, it is instantiated with empty qualifier.
            var instantiatedModule = InstantiateModuleWithDefaultQualifier(moduleLiteral);

            // Decide here whether to use decoration for the config evaluation phase.
            // Let's say no decorators allowed at this stage.
            IDecorator<EvaluationResult> decoratorForConfig = null;

            // Create an evaluation context tree and root context.
            using (var contextTree = CreateContext(instantiatedModule, decoratorForConfig, EvaluatorConfigurationForConfig, FileType.GlobalConfiguration))
            {
                var context = contextTree.RootContext;

                if (instantiatedModule.IsEmpty)
                {
                    return null;
                }

                // Note: Blocking on evaluation
                var success = instantiatedModule.EvaluateAllAsync(context, VisitedModuleTracker.Disabled).GetAwaiter().GetResult();

                if (!success)
                {
                    // Error has been reported during the evaluation.
                    return null;
                }

                return configObjectLiteral == null ? ResolveConfigObjectLiteral(instantiatedModule, context) 
                    : (ObjectLiteral)configObjectLiteral.Merge(context, EvaluationStackFrame.UnsafeFrom(new EvaluationResult[0]), new EvaluationResult(ResolveConfigObjectLiteral(instantiatedModule, context))).Value;
            }
        }

        /// <summary>
        /// Tries to find the object literal associated to the configuration.
        /// Since we actually support two config keywords (the legacy one is there for compat reasons), we need to check both cases
        /// Returns null if the result cannot be casted to an object literal.
        /// </summary>
        private ObjectLiteral ResolveConfigObjectLiteral(ModuleLiteral instantiatedModule, Context context)
        {
            var bindings = instantiatedModule.GetAllBindings(context).ToList();
            Contract.Assert(bindings.Count == 1, "Expected AstConverter to produce exactly one binding in the resulting ModuleLiteral when converting a config file");

            var binding = bindings.Single();
            return instantiatedModule
                .GetOrEvalFieldBinding(
                    context,
                    SymbolAtom.Create(context.StringTable, Script.Constants.Names.ConfigurationFunctionCall),
                    binding.Value,
                    instantiatedModule.Location)
                .Value as ObjectLiteral;
        }
    }
}
