// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities.Core;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <remarks>
    /// Had to add a suffix to the name to maintain the pattern of class Foo : IFoo Configuration conflicts with the namespace.
    /// Could have done
    /// </remarks>
    [SuppressMessage("Microsoft.Naming", "CA1711:IdentifiersShouldNotHaveIncorrectSuffix")]
    public class ConfigurationImpl : RootModuleConfiguration, IConfiguration
    {
        /// <nodoc />
        public ConfigurationImpl()
        {
            Qualifiers = new QualifierConfiguration();
            Resolvers = new List<IResolverSettings>();
            AllowedEnvironmentVariables = new List<string>();
            Layout = new LayoutConfiguration();
            Engine = new EngineConfiguration();
            Schedule = new ScheduleConfiguration();
            Sandbox = new SandboxConfiguration();
            Cache = new CacheConfiguration();
            Logging = new LoggingConfiguration();
            Experiment = new ExperimentalConfiguration();
            Distribution = new DistributionConfiguration();
            Projects = null; // Deliberate null, here as magic indication that none has been defined. All consumers are aware and deal with it.
            Packages = null; // Deliberate null, here as magic indication that none has been defined. All consumers are aware and deal with it.
            Modules = null; // Deliberate null, here as magic indication that none has been defined. All consumers are aware and deal with it.
            FrontEnd = new FrontEndConfiguration();
            CommandLineEnabledUnsafeOptions = new List<string>();
            Ide = new IdeConfiguration();
            ResolverDefaults = new ResolverDefaults();
            Infra = Infra.Developer;
        }

        /// <summary>
        /// Create new mutable instance from template
        /// </summary>
        /// <remarks>
        /// This is the only class with CommandLineConfiguration as the configuration entrypoint where the pathRemapper is an argument with default value.
        /// If the argument was optional everywhere as well there would be no compiler helping us if someone forgot to pass it along.
        /// This is the main entrypoint so we allow a default value here for convenience
        /// </remarks>
        public ConfigurationImpl(IConfiguration template, PathRemapper pathRemapper = null)
            : base(template, pathRemapper = pathRemapper ?? new PathRemapper())
        {
            Contract.Assume(template != null);

            Qualifiers = new QualifierConfiguration(template.Qualifiers);
            Resolvers = new List<IResolverSettings>(template.Resolvers.Select(resolver => ResolverSettings.CreateFromTemplate(resolver, pathRemapper)));
            AllowedEnvironmentVariables = new List<string>(template.AllowedEnvironmentVariables);
            Layout = new LayoutConfiguration(template.Layout, pathRemapper);
            Engine = new EngineConfiguration(template.Engine, pathRemapper);
            Schedule = new ScheduleConfiguration(template.Schedule, pathRemapper);
            Sandbox = new SandboxConfiguration(template.Sandbox, pathRemapper);
            Cache = new CacheConfiguration(template.Cache, pathRemapper);
            Logging = new LoggingConfiguration(template.Logging, pathRemapper);
            Experiment = new ExperimentalConfiguration(template.Experiment);
            Distribution = new DistributionConfiguration(template.Distribution);
            Projects = template.Projects?.Select(p => pathRemapper.Remap(p)).ToList();
            Packages = template.Packages?.Select(p => pathRemapper.Remap(p)).ToList();
            Modules = template.Modules?.Select(m => pathRemapper.Remap(m)).ToList();
            DisableDefaultSourceResolver = template.DisableDefaultSourceResolver;
            DisableInBoxSdkSourceResolver = template.DisableInBoxSdkSourceResolver;
            FrontEnd = new FrontEndConfiguration(template.FrontEnd, pathRemapper);
            CommandLineEnabledUnsafeOptions = new List<string>(template.CommandLineEnabledUnsafeOptions);
            Ide = new IdeConfiguration(template.Ide, pathRemapper);
            Interactive = template.Interactive;
            ResolverDefaults = new ResolverDefaults(template.ResolverDefaults);
            Infra = template.Infra;
        }

        /// <inheritdoc />
        public void MarkIConfigurationMembersInvalid()
        {
            m_isInvalidated = true;
        }

        private bool m_isInvalidated;

        /// <nodoc />
        public void AssertNotInvalid()
        {
            Contract.Assert(!m_isInvalidated, "Attempted to use an invalidated configuration. This indicates a programming error. Contact domdev");
        }

        /// <inheritdoc />
        public bool? DisableDefaultSourceResolver
        {
            get
            {
                AssertNotInvalid();
                return m_disableDefaultSourceResolver;
            }

            set
            {
                m_disableDefaultSourceResolver = value;
            }
        }

        private bool? m_disableDefaultSourceResolver;

        /// <nodoc />
        public FrontEndConfiguration FrontEnd
        {
            get
            {
                AssertNotInvalid();
                return m_frontEndConfiguration;
            }

            set
            {
                m_frontEndConfiguration = value;
            }
        }

        private FrontEndConfiguration m_frontEndConfiguration;

        /// <inheritdoc />
        IFrontEndConfiguration IConfiguration.FrontEnd => FrontEnd;

        /// <nodoc />
        public QualifierConfiguration Qualifiers
        {
            get
            {
                AssertNotInvalid();
                return m_qualifiers;
            }

            set
            {
                m_qualifiers = value;
            }
        }

        private QualifierConfiguration m_qualifiers;

        /// <inhertidoc />
        IQualifierConfiguration IConfiguration.Qualifiers => Qualifiers;

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<IResolverSettings> Resolvers
        {
            get
            {
                AssertNotInvalid();
                return m_resolvers;
            }

            set
            {
                m_resolvers = value;
            }
        }

        private List<IResolverSettings> m_resolvers;

        /// <inhertidoc />
        IReadOnlyList<IResolverSettings> IConfiguration.Resolvers => Resolvers;

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<string> AllowedEnvironmentVariables
        {
            get
            {
                AssertNotInvalid();
                return m_allowedEnvironmentVariables;
            }

            set
            {
                m_allowedEnvironmentVariables = value;
            }
        }

        private List<string> m_allowedEnvironmentVariables;

        /// <inhertidoc />
        IReadOnlyList<string> IConfiguration.AllowedEnvironmentVariables => AllowedEnvironmentVariables;

        /// <nodoc />
        public LayoutConfiguration Layout
        {
            get
            {
                AssertNotInvalid();
                return m_layout;
            }

            set
            {
                m_layout = value;
            }
        }

        private LayoutConfiguration m_layout;

        /// <inhertidoc />
        ILayoutConfiguration IConfiguration.Layout => Layout;

        /// <nodoc />
        public EngineConfiguration Engine
        {
            get
            {
                AssertNotInvalid();
                return m_engine;
            }

            set
            {
                m_engine = value;
            }
        }

        private EngineConfiguration m_engine;

        /// <inhertidoc />
        IEngineConfiguration IConfiguration.Engine => Engine;

        /// <nodoc />
        public ScheduleConfiguration Schedule
        {
            get
            {
                AssertNotInvalid();
                return m_schedule;
            }

            set
            {
                m_schedule = value;
            }
        }

        private ScheduleConfiguration m_schedule;

        /// <inhertidoc />
        IScheduleConfiguration IConfiguration.Schedule => Schedule;

        /// <nodoc />
        public SandboxConfiguration Sandbox
        {
            get
            {
                AssertNotInvalid();
                return m_sandbox;
            }

            set
            {
                m_sandbox = value;
            }
        }

        private SandboxConfiguration m_sandbox;

        /// <inhertidoc />
        ISandboxConfiguration IConfiguration.Sandbox => Sandbox;

        /// <nodoc />
        public CacheConfiguration Cache
        {
            get
            {
                AssertNotInvalid();
                return m_cache;
            }

            set
            {
                m_cache = value;
            }
        }

        private CacheConfiguration m_cache;

        /// <inhertidoc />
        ICacheConfiguration IConfiguration.Cache => Cache;

        /// <nodoc />
        public LoggingConfiguration Logging
        {
            get
            {
                AssertNotInvalid();
                return m_logging;
            }

            set
            {
                m_logging = value;
            }
        }

        private LoggingConfiguration m_logging;

        /// <inhertidoc />
        ILoggingConfiguration IConfiguration.Logging => Logging;

        /// <nodoc />
        public ExperimentalConfiguration Experiment
        {
            get
            {
                AssertNotInvalid();
                return m_experiment;
            }

            set
            {
                m_experiment = value;
            }
        }

        private ExperimentalConfiguration m_experiment;

        /// <inhertidoc />
        IExperimentalConfiguration IConfiguration.Experiment => Experiment;

        /// <nodoc />
        public DistributionConfiguration Distribution
        {
            get
            {
                AssertNotInvalid();
                return m_distribution;
            }

            set
            {
                m_distribution = value;
            }
        }

        private DistributionConfiguration m_distribution;

        /// <inhertidoc />
        IDistributionConfiguration IConfiguration.Distribution => Distribution;

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<AbsolutePath> Projects
        {
            get
            {
                AssertNotInvalid();
                return m_projects;
            }

            set
            {
                m_projects = value;
            }
        }

        private List<AbsolutePath> m_projects;

        /// <inhertidoc />
        IReadOnlyList<AbsolutePath> IConfiguration.Projects => Projects;

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<AbsolutePath> Packages
        {
            get
            {
                AssertNotInvalid();
                return m_packages;
            }

            set
            {
                m_packages = value;
            }
        }

        private List<AbsolutePath> m_packages;

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public IReadOnlyList<AbsolutePath> Modules
        {
            get
            {
                AssertNotInvalid();
                return m_modules;
            }

            set
            {
                m_modules = value;
            }
        }

        private IReadOnlyList<AbsolutePath> m_modules;

        /// <inhertidoc />
        IReadOnlyList<AbsolutePath> IConfiguration.Packages => Packages;

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<string> CommandLineEnabledUnsafeOptions
        {
            get
            {
                AssertNotInvalid();
                return m_unsafeOptionsEnabled;
            }

            set
            {
                m_unsafeOptionsEnabled = value;
            }
        }

        private List<string> m_unsafeOptionsEnabled;

        /// <inhertidoc />
        IReadOnlyList<string> IConfiguration.CommandLineEnabledUnsafeOptions => CommandLineEnabledUnsafeOptions;

        /// <nodoc />
        public IdeConfiguration Ide
        {
            get
            {
                AssertNotInvalid();
                return m_ideConfiguration;
            }

            set
            {
                m_ideConfiguration = value;
            }
        }

        /// <nodoc />
        // Temporary redirect for back compat
        [Obsolete]
        public IdeConfiguration VsDomino { 
            get { return Ide;} 
            set { Ide = value;}
        }

        private IdeConfiguration m_ideConfiguration;

        /// <inheritdoc />
        IIdeConfiguration IConfiguration.Ide => Ide;

        /// <summary>
        /// This property is no longer a part of the interface, but it's preserved here for backward compatibility.
        /// Our logic for parsing configuration specified in the spec files is doing some reflection magic, so if
        /// a user has 'inCloudBuild' in their specs, that said magic will fail if this property is missing.
        /// </summary>
        [ObsoleteAttribute()]
        public bool? InCloudBuild { get; set; }

        /// <inheritdoc />
        public bool Interactive { get; set; }

        /// <nodoc />
        public ResolverDefaults ResolverDefaults
        {
            get
            {
                AssertNotInvalid();
                return m_resolverDefaults;
            }
            set => m_resolverDefaults = value;
        }

        private ResolverDefaults m_resolverDefaults;

        /// <inheritdoc />
        IResolverDefaults IConfiguration.ResolverDefaults => ResolverDefaults;

        /// <inheritdoc/>
        public bool? DisableInBoxSdkSourceResolver { get; set; }

        /// <inheritdoc/>
        public Infra Infra { get; set; }
    }
}
