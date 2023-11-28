// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.ToolSupport;
using BuildXL.Utilities.Core;

namespace BuildXL.AdoBuildRunner
{
    /// <summary>
    /// Argument parsing logic for the ADOBuildRunner
    /// </summary>
    public sealed class Args
    {
        private readonly Vsts.Logger m_logger;

        /// <nodoc/>
        public Args(Vsts.Logger logger)
        {
            m_logger = logger;
        }

        /// <nodoc />
        public static bool TryParseArguments(Vsts.Logger logger, string[] args, out IAdoBuildRunnerConfiguration configuration, out IReadOnlyCollection<string> forwardingArguments)
        {
            var argsParser = new Args(logger);
            return argsParser.TryParse(args, out configuration, out forwardingArguments);
        }

        /// <summary>
        /// Parse provided <paramref name="args"/> and populate with them a <paramref name="configuration"/> object.
        /// </summary>
        /// <remarks>
        /// Only <see cref="ArgumentOption.CallerArguments"/> are parsed. The <see cref="ArgumentOption.ForwardingArguments"/> are returned in <paramref name="forwardingArguments"/>
        /// </remarks>
        public bool TryParse(string[] args, out IAdoBuildRunnerConfiguration configuration, out IReadOnlyCollection<string> forwardingArguments)
        {
            try
            {
                var commandLineUtilities = new CommandLineUtilities(args);

                var mutableConfig = new AdoBuildRunnerConfiguration();
                var cacheConfigGenerationConfiguration = mutableConfig.CacheConfigGenerationConfiguration;

                OptionHandler[] handlers = new [] 
                {
                    OptionHandlerFactory.CreateOption(
                            "cacheConfigCacheSizeInMB",
                            opt => cacheConfigGenerationConfiguration.CacheSizeInMB = CommandLineUtilities.ParseInt32Option(opt, 0, int.MaxValue)),
                    OptionHandlerFactory.CreateOption(
                            "cacheConfigCacheType",
                            opt => cacheConfigGenerationConfiguration.CacheType = CommandLineUtilities.ParseEnumOption<CacheType>(opt)),
                    OptionHandlerFactory.CreateBoolOption(
                            "cacheConfigLogGeneratedConfiguration",
                            opt => cacheConfigGenerationConfiguration.LogGeneratedConfiguration = opt),
                    OptionHandlerFactory.CreateOption(
                            "cacheConfigManagedIdentityId",
                            opt => cacheConfigGenerationConfiguration.ManagedIdentityId = CommandLineUtilities.ParseGuidOption(opt)),
                    OptionHandlerFactory.CreateOption(
                            "cacheConfigRetentionPolicyInDays",
                            opt => cacheConfigGenerationConfiguration.RetentionPolicyInDays = CommandLineUtilities.ParseInt32Option(opt, 0, int.MaxValue)),
                    OptionHandlerFactory.CreateOption(
                            "cacheConfigStorageAccountEndpoint",
                            opt => cacheConfigGenerationConfiguration.StorageAccountEndpoint = CommandLineUtilities.ParseUriOption(opt)),
                    OptionHandlerFactory.CreateOption(
                            "cacheConfigUniverse",
                            opt => cacheConfigGenerationConfiguration.Universe = opt.Value),
                }.SelectMany(x => x)
                 .OrderBy(opt => opt.OptionName, StringComparer.OrdinalIgnoreCase)
                 .ToArray();

                configuration = mutableConfig;

                // TODO: remove this once all the arguments that are now passed via environment variables are moved to be command line arguments and all callers are updated. After that 
                // is completed, the expectation is that there will always be a '--' argument being passed to the runner. For the time being, if that's not the
                // case assume all arguments are forwarding arguments.
                if (!commandLineUtilities.HasEndOfCallerArgument)
                {
                    forwardingArguments = args;
                    return true;
                }

                // Let's retrieve the caller arguments options only
                if (!OptionHandlerMatcher.TryMatchAndExecuteHandler(commandLineUtilities.GetOptions(ArgumentOption.CallerArguments), handlers, out var unrecognizedOption))
                {
                    throw new InvalidArgumentException($"Unrecognized option: {unrecognizedOption.Name}");
                }

                configuration.ValidateConfiguration();

                forwardingArguments = commandLineUtilities.GetExpandedArguments(ArgumentOption.ForwardingArguments);

                return true;
            }
            catch (Exception ex)
            {
                m_logger.Error(ex.GetLogEventMessage());
                m_logger.Error(Environment.CommandLine);

                configuration = null;
                forwardingArguments = null;

                return false;
            }
        }
    }
}
