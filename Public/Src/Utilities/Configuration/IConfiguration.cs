// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using JetBrains.Annotations;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// The overall BuildXL Configuration
    /// </summary>
    public interface IConfiguration : IRootModuleConfiguration
    {
        /// <summary>
        /// Marks members of IConfiguration as invalid so nothing can consume out of date members.
        /// </summary>
        /// <remarks>
        /// The need for this is a bit of a design issue with how we construct these configuration objects. But while we
        /// have the model of making copies of the configuration object we need a way to enforce that out of date copies
        /// are no longer used
        /// </remarks>
        void MarkIConfigurationMembersInvalid();

        /// <summary>
        /// Configuration settings for Qualifiers
        /// </summary>
        [NotNull]
        IQualifierConfiguration Qualifiers { get; }

        /// <summary>
        /// Registration of resolvers that can find packages
        /// </summary>
        /// <remarks>
        /// This is the configuration where the settings apply.
        /// </remarks>
        [CanBeNull]
        IReadOnlyList<IResolverSettings> Resolvers { get; }

        /// <summary>
        /// The environment variables that are accessible in the build.
        /// </summary>
        [NotNull]
        IReadOnlyList<string> AllowedEnvironmentVariables { get; }

        /// <summary>
        /// Layout configuration
        /// </summary>
        [NotNull]
        ILayoutConfiguration Layout { get; }

        /// <summary>
        /// Engine configuration settings
        /// </summary>
        [NotNull]
        IEngineConfiguration Engine { get; }

        /// <summary>
        /// Scheduling configuration settings
        /// </summary>
        [NotNull]
        IScheduleConfiguration Schedule { get; }

        /// <summary>
        /// Process sandbox configuration settings
        /// </summary>
        [NotNull]
        ISandboxConfiguration Sandbox { get; }

        /// <summary>
        /// Cache configuration settings
        /// </summary>
        [NotNull]
        ICacheConfiguration Cache { get; }

        /// <summary>
        /// Logging configuration settings
        /// </summary>
        [NotNull]
        ILoggingConfiguration Logging { get; }

        /// <summary>
        /// Export configuration settings
        /// </summary>
        [NotNull]
        IExportConfiguration Export { get; }

        /// <summary>
        /// Experimental settings
        /// </summary>
        [NotNull]
        IExperimentalConfiguration Experiment { get; }

        /// <summary>
        /// Distribution configuration settings
        /// </summary>
        [NotNull]
        IDistributionConfiguration Distribution { get; }

        /// <summary>
        /// Controls viewer behavior. Allowed values are 'Show', 'Hide', and 'Disable'. Default is 'Hide'.
        /// </summary>
        ViewerMode Viewer { get; }

        /// <summary>
        /// Projects in this build organization that are not owned by any package.
        /// </summary>
        /// <remarks>
        /// If this field is not specified (null), then all orphan projects (i.e., projects that do not belong
        /// to any user-specified packages) in the cone of the configuration are owned by the configuration. (The cone
        /// of a configuration is the directory containing the configuration file including all sub-directories underneath.)
        /// When users evaluate or build the configuration, all orphan projects owned by the configuration are evaluated.
        /// This field can be used to restrict the set of orphan projects that the configuration owns.
        /// All projects mentioned in this field must be orphan and must be in the cone of the configuration.
        /// </remarks>
        [CanBeNull]
        IReadOnlyList<AbsolutePath> Projects { get; }

        /// <summary>
        /// Packages in this build organization that are owned by the configuration.
        /// </summary>
        /// <remarks>
        /// Obsolete but kept for back-compat reasons. See <see cref="Modules"/>.
        /// </remarks>
        [CanBeNull]
        IReadOnlyList<AbsolutePath> Packages { get; }

        /// <summary>
        /// Modules in this build organization that are owned by the configuration.
        /// </summary>
        /// <remarks>
        /// If this field is not specified (null), then all modules in the cone of the configuration are owned by
        /// the configuration. (The cone of a configuration is the directory containing the configuration file including
        /// all sub-directories underneath.) When users evaluate or build the configuration, all modules owned by
        /// the configuration are evaluated. This field can be used to restrict the set of modules that the configuration owns.
        /// All modules mentioned in this field must be in the cone of the configuration.
        /// </remarks>
        IReadOnlyList<AbsolutePath> Modules { get; }

        /// <summary>
        /// Disable the default source resolver (as the last considered resolver) when set to true.
        /// </summary>
        /// <remarks>
        /// The default source resolver are inferred from the packages that the configuration owns. If this field is set
        /// to true, then the default source resolver simply give up that ownership, and as a consequence, will not include those packages
        /// in its look-up list. In addition, if there is no package in the directory where the configuration lives, a distinct (or magic) package
        /// is introduced in that directory. This magic package is used as the package for the orphan projects (i.e., projects that are not owned
        /// by any package).
        /// </remarks>
        bool? DisableDefaultSourceResolver { get; }

        /// <summary>
        /// Configuration for front end.
        /// </summary>
        IFrontEndConfiguration FrontEnd { get; }

        /// <summary>
        /// The unsafe options that are explicitly enabled by command line
        /// </summary>
        [NotNull]
        IReadOnlyList<string> CommandLineEnabledUnsafeOptions { get; }

        /// <summary>
        /// Configuration for Ide Generator
        /// </summary>
        IIdeConfiguration Ide { get; }

        /// <summary>
        /// Whether this build is running in CloudBuild
        /// </summary>
        /// <remarks>
        /// Q: Isn't that what the telemetry environment is for?
        /// A: Not exactly. That "environment" has evolved into being more of a customer/application identifier. Where the build is
        /// running is a different dimension. Otherwise all environments would be duplicated once they move to CloudBuild.
        /// Also, CloudBuild is a very special environment. So BuildXL may benefit from awareness that it is run in that
        /// setting in order to enable/disable various features.
        /// </remarks>
        bool? InCloudBuild { get; }

        /// <summary>
        /// Whether BuildXL is allowed to interact with the user either via console or popups.
        /// A common use case is to allow front ends like nuget to display authentication prompts in case the user is not authenticated.
        /// This defaults to false, and should never be set to true when running in an unattended lab or cloud environment as it can potentially hang the build.
        /// </summary>
        bool Interactive { get; }

        /// <summary>
        /// Default configuration parameters for front-end resolvers.
        /// </summary>
        IResolverDefaults ResolverDefaults { get; }
    }
}
