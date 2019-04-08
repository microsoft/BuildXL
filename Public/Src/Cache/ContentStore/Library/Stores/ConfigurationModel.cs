// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    ///     ContentStore configuration selection.
    /// </summary>
    public enum ConfigurationSelection
    {
        /// <summary>
        ///     In-process configuration is used only if configuration file is not present.
        /// </summary>
        UseFileAllowingInProcessFallback,

        /// <summary>
        ///     In-process configuration is required and used, ignoring existing configuration file.
        /// </summary>
        RequireAndUseInProcessConfiguration
    }

    /// <summary>
    ///     How to handle missing configuration file.
    /// </summary>
    public enum MissingConfigurationFileOption
    {
        /// <summary>
        ///     Do not write configuration file from in-process configuration.
        /// </summary>
        DoNotWrite,

        /// <summary>
        ///     Write configuration file from in-process configuration only if file does not yet exist.
        /// </summary>
        WriteOnlyIfNotExists
    }

    /// <summary>
    ///     Encapsulation of details around handling of in-process configuration.
    /// </summary>
    public class ConfigurationModel
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="ConfigurationModel"/> class.
        /// </summary>
        public ConfigurationModel(
            ContentStoreConfiguration inProcessConfiguration = null,
            ConfigurationSelection selection = ConfigurationSelection.UseFileAllowingInProcessFallback,
            MissingConfigurationFileOption missingFileOption = MissingConfigurationFileOption.DoNotWrite)
        {
            InProcessConfiguration = inProcessConfiguration;
            Selection = selection;
            MissingFileOption = missingFileOption;
        }

        /// <summary>
        ///     Gets optional in-process configuration.
        /// </summary>
        public ContentStoreConfiguration InProcessConfiguration { get; }

        /// <summary>
        ///     Gets configuration selection method.
        /// </summary>
        public ConfigurationSelection Selection { get; }

        /// <summary>
        ///     Gets method for handling missing configuration file.
        /// </summary>
        public MissingConfigurationFileOption MissingFileOption { get; }
    }
}
