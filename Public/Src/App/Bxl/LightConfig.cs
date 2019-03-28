// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;

namespace BuildXL
{
    /// <summary>
    /// The lightweight configuration that is the minimal set of things needed to initialize the client process
    /// </summary>
    /// <remarks>
    /// This exists because the standard argument parser has bloated to a very complicated thing that takes a
    /// significant time to JIT
    /// </remarks>
    internal sealed class LightConfig
    {
        /// <summary>
        /// CAUTION!!! - When anything is added, make sure to updated LightConfigTests.AssertCongruent() to make sure this always
        /// stays in sync with the full blown configuration
        /// </summary>
        #region options
        public ServerMode Server = OperatingSystemHelper.IsUnixOS ? ServerMode.Disabled : ServerMode.Enabled;
        public string ServerDeploymentDirectory;
        public string Config;
        public bool NoLogo;
        public BuildXL.Utilities.Configuration.HelpLevel Help;
        public int HelpCode;
        public bool FancyConsole = true;
        public bool Color = true;
        public string SubstTarget;
        public string SubstSource;
        public bool DisablePathTranslation;
        public bool EnableDedup;

        // Strangely, this is not configurable via Args.cs
        public bool AnimateTaskbar = true;
        public int ServerIdleTimeInMinutes = 60;
        #endregion

        private LightConfig() { }

        /// <summary>
        /// Attempts to parse the configuration. This is a subset of what's parsed in Args
        /// </summary>
        /// <remarks>
        /// Keep the subset of parsing here limited to whatever's needed to run the client process
        /// </remarks>
        public static bool TryParse(string[] args, out LightConfig lightConfig)
        {
            lightConfig = new LightConfig();

            var cl = new CommandLineUtilities(args);
            foreach (var option in cl.Options)
            {
                switch (option.Name.ToUpperInvariant())
                {
                    case "C":
                    case "CONFIG":
                        lightConfig.Config = CommandLineUtilities.ParseStringOption(option);
                        break;
                    case "COLOR":
                    case "COLOR+":
                    case "COLOR-":
                        lightConfig.Color = CommandLineUtilities.ParseBooleanOption(option);
                        break;
                    case "DISABLEPATHTRANSLATION":
                        lightConfig.DisablePathTranslation = true;
                        break;
                    case "HELP":
                        var help = Args.ParseHelpOption(option);
                        lightConfig.Help = help.Key;
                        lightConfig.HelpCode = help.Value;
                        break;
                    case "NOLOGO":
                    case "NOLOGO+":
                    case "NOLOGO-":
                        lightConfig.NoLogo = CommandLineUtilities.ParseBooleanOption(option);
                        break;
                    case "FANCYCONSOLE":
                    case "FANCYCONSOLE+":
                    case "FANCYCONSOLE-":
                    case "EXP:FANCYCONSOLE":
                    case "EXP:FANCYCONSOLE+":
                    case "EXP:FANCYCONSOLE-":
                        lightConfig.FancyConsole = CommandLineUtilities.ParseBooleanOption(option);
                        break;
                    case "ENABLEDEDUP":
                    case "ENABLEDEDUP+":
                    case "ENABLEDEDUP-":
                        lightConfig.EnableDedup = CommandLineUtilities.ParseBooleanOption(option);
                        break;
                    case "SERVER":
                        lightConfig.Server = CommandLineUtilities.ParseBoolEnumOption(option, true, ServerMode.Enabled, ServerMode.Disabled);
                        break;
                    case "SERVER+":
                        lightConfig.Server = CommandLineUtilities.ParseBoolEnumOption(option, true, ServerMode.Enabled, ServerMode.Disabled);
                        break;
                    case "SERVER-":
                        lightConfig.Server = CommandLineUtilities.ParseBoolEnumOption(option, false, ServerMode.Enabled, ServerMode.Disabled);
                        break;
                    case "SERVERMAXIDLETIMEINMINUTES":
                        lightConfig.ServerIdleTimeInMinutes = CommandLineUtilities.ParseInt32Option(option, 1, int.MaxValue);
                        break;
                    case "SERVERDEPLOYMENTDIR":
                        lightConfig.ServerDeploymentDirectory = CommandLineUtilities.ParseStringOption(option);
                        break;
                    case "SUBSTSOURCE":
                        lightConfig.SubstSource = CommandLineUtilities.ParseStringOption(option);
                        break;
                    case "SUBSTTARGET":
                        lightConfig.SubstTarget = CommandLineUtilities.ParseStringOption(option);
                        break;
                }
            }

            // This has no attempt at any sort of error handling. Leave that to the real argument parser. Only detect errors
            return !string.IsNullOrWhiteSpace(lightConfig.Config);
        }
    }
}
