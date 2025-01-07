// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.ProcessPipExecutor;
using BuildXL.Scheduler;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Scheduler.Fingerprints;

namespace BuildXL.Engine
{
    /// <summary>
    /// State from the Configuration needed by EngineSchedule
    /// </summary>
    public sealed class ConfigFileState
    {
        /// <summary>
        /// Envelope for serialization
        /// </summary>
        public static readonly FileEnvelope FileEnvelope = new FileEnvelope(name: "ConfigFileState", version: 0);

        /// <summary>
        /// Default pip filter from config file, if any
        /// </summary>
        public readonly string DefaultPipFilter;

        /// <summary>
        /// FileAccessAllowlist from config.
        /// </summary>
        public readonly FileAccessAllowlist FileAccessAllowlist;

        /// <summary>
        /// Reclassification rules
        /// </summary>
        public readonly ObservationReclassifier GlobalReclassificationRules;

        /// <summary>
        /// Directory enumeration fingerprinter exceptions from the config
        /// </summary>
        public readonly DirectoryMembershipFingerprinterRuleSet DirectoryMembershipFingerprinterRules;

        /// <summary>
        /// Map from module id to module name
        /// </summary>
        public readonly IList<IModuleConfiguration> ModuleConfigurations;

        /// <summary>
        /// Constructor
        /// </summary>
        public ConfigFileState(
            FileAccessAllowlist fileAccessAllowlist,
            string defaultPipFilter,
            DirectoryMembershipFingerprinterRuleSet directoryMembershipFingerprinterRules,
            IList<IModuleConfiguration> moduleConfigurations,
            ObservationReclassifier globalReclassificationRules)
        {
            FileAccessAllowlist = fileAccessAllowlist;
            DefaultPipFilter = defaultPipFilter ?? string.Empty;
            DirectoryMembershipFingerprinterRules = directoryMembershipFingerprinterRules;
            ModuleConfigurations = moduleConfigurations;
            GlobalReclassificationRules = globalReclassificationRules;
        }

        /// <summary>
        /// Serializes
        /// </summary>
        public void Serialize(BuildXLWriter writer)
        {
            Contract.Requires(writer != null);

            FileAccessAllowlist.Serialize(writer);
            GlobalReclassificationRules.Serialize(writer);
            writer.Write(DefaultPipFilter);
            DirectoryMembershipFingerprinterRules.Serialize(writer);

            writer.WriteCompact(ModuleConfigurations.Count);
            foreach (var moduleConfiguration in ModuleConfigurations)
            {
                writer.Write(moduleConfiguration.ModuleId);
                writer.Write(moduleConfiguration.Name);

                // TODO: Write everything else instead of doing it in many different places?
            }
        }

        /// <summary>
        /// Deserializes
        /// </summary>
        public static async Task<ConfigFileState> DeserializeAsync(
            BuildXLReader reader,
            Task<PipExecutionContext> contextTask)
        {
            Contract.Requires(reader != null);
            Contract.Requires(contextTask != null);

            FileAccessAllowlist allowlist = await FileAccessAllowlist.DeserializeAsync(reader, contextTask);
            var reclassificationRules = ObservationReclassifier.Deserialize(reader);
            string defaultFilter = reader.ReadString();
            DirectoryMembershipFingerprinterRuleSet ruleSet = DirectoryMembershipFingerprinterRuleSet.Deserialize(reader);

            int moduleConfigurationsCount = reader.ReadInt32Compact();
            List<IModuleConfiguration> moduleConfigurations = new List<IModuleConfiguration>(moduleConfigurationsCount);
            for (int i = 0; i < moduleConfigurationsCount; i++)
            {
                var moduleConfiguration = new BuildXL.Utilities.Configuration.Mutable.ModuleConfiguration()
                {
                    ModuleId = reader.ReadModuleId(),
                    Name = reader.ReadString(),
                };

                moduleConfigurations.Add(moduleConfiguration);
            }

            // TODO: Read everything else instead of doing it in many different places?
            return new ConfigFileState(allowlist, defaultFilter, ruleSet, moduleConfigurations, reclassificationRules);
        }

        /// <summary>
        /// Until we have unified frontends we'll need to post-merge some of the stuff from the .dc file into the supposedly immutable configuration object.
        /// </summary>
        internal IConfiguration MergeIntoConfiguration(BuildXL.Utilities.Configuration.Mutable.ConfigurationImpl mutableConfig)
        {
            if (!string.IsNullOrEmpty(DefaultPipFilter))
            {
                mutableConfig.Engine.DefaultFilter = DefaultPipFilter;
            }

            // This codepath is for a cached build, so we are not setting the allowlists on the configuration object.
            // Because the FileAccessAllowList on this is the serialized version will be used to construct the schedule, and it
            // is not constructed from the config. So it okay to leave it off here.

            // Engine settings for allowlists.
            // This update of the configuration is currently duplicated here, in Engine.PopulateConfiguration.
            // The reason is that the codepaths for a) New config, b) Parsing BuildXL xml config and c) loading cached graph
            // don't come together soon enough to hold the only modify the configuration once.
            // This should be short-term until all the frontends are properly unified.

            // Set the global settings for failing unexpected file accesses to false if any of the allowlists (global and per-module) are non-empty.
            if (FileAccessAllowlist != null)
            {
                IEnumerable<FileAccessAllowlist> fileAccessAllowlists = new[] { FileAccessAllowlist };
                if (FileAccessAllowlist.ModuleAllowlists != null && FileAccessAllowlist.ModuleAllowlists.Count != 0)
                {
                    fileAccessAllowlists = fileAccessAllowlists.Concat(FileAccessAllowlist.ModuleAllowlists.Values);
                }

                foreach (var fileAccessAllowlist in fileAccessAllowlists)
                {
                    if ((fileAccessAllowlist.CacheableEntryCount + fileAccessAllowlist.UncacheableEntryCount) > 0)
                    {
                        mutableConfig.Sandbox.FailUnexpectedFileAccesses = false;
                    }
                }
            }

            return mutableConfig;
        }
    }
}
