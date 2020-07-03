// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using BuildXL.Native.IO;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Tracing;
using BuildXL.ToolSupport;
using BuildXL.Utilities.Collections;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer JavaScriptDependencyFixerAnalyzer()
        {
            return new JavaScriptDependencyFixerAnalyzer(GetAnalysisInput());
        }

        private static void WriteJavaScriptDependencyFixerHelp(HelpWriter writer)
        {
            writer.WriteBanner("JavaScript Dependency Fixer \"Analyzer\"");
            writer.WriteModeOption(nameof(AnalysisMode.JavaScriptDependencyFixer), 
                "Fixes missing JavaScript project dependencies based on file monitoring violations.");
        }
    }

    /// <summary>
    /// Adds missing dependencies based on DFAs for JavaScript projects
    /// </summary>
    /// <remarks>
    /// It makes some assumptions related to how pips were scheduled. In particular, this fixer works well with the Rush frontend. The assumptions are:
    /// * The working directory of pips represent the root of a JavaScript project
    /// * The pip was scheduled so AllowedUndeclaredSourceRead is on. This means that a missing declared dependency manifests as a write in an undeclared
    /// source read.
    /// The updates will be made under 'devDependencies' on the corresponding package.json. A human needs to review these updates and potentially move any
    /// dependencies under 'dependencies'.
    /// </remarks>
    public class JavaScriptDependencyFixerAnalyzer : Analyzer
    {
        private readonly Dictionary<Process, HashSet<Process>> m_missingDependencies = new Dictionary<Process, HashSet<Process>>();

        /// <nodoc/>
        public JavaScriptDependencyFixerAnalyzer(AnalysisInput input) : base(input)
        {
        }

        /// <inheritdoc/>
        public override int Analyze()
        {
            int packageUpdatedCount = 0;
            foreach (var reader in m_missingDependencies.Keys)
            {
                if (UpdatePackageJsonIfNeeded(reader, m_missingDependencies[reader]))
                {
                    packageUpdatedCount++;
                }
            }    

            if (m_missingDependencies.Count == 0)
            {
                Console.WriteLine("No actionable violations were recorded in the BuildXL binary log file");
            }

            Console.Out.WriteLine($"{packageUpdatedCount} packages updated.");

            return 0;
        }

        /// <summary>
        /// Updates the corresponding package.json of <paramref name="reader"/> based on <paramref name="missingDependencies"/>
        /// </summary>
        /// <remarks>
        /// Assumes pips are scheduled such that the working directory is where package.json is. This is the case for example
        /// for the Rush resolver.
        /// </remarks>
        private bool UpdatePackageJsonIfNeeded(Process reader, HashSet<Process> missingDependencies)
        {
            // Package.json should be at the root of the working directory
            var packageJsonPath = reader.WorkingDirectory.Combine(PathTable, "package.json").ToString(PathTable);
            Console.WriteLine($"Analyzing {packageJsonPath}");

            if (!FileUtilities.Exists(packageJsonPath))
            {
                Console.WriteLine($"{packageJsonPath} does not exist. Skipping.");
                return false;
            }

            // Read the package.json file and retrieve the current devDependencies if any
            Dictionary<string, object> packageJson;
            try
            {
                packageJson = JsonConvert.DeserializeObject<Dictionary<string, object>>(File.ReadAllText(packageJsonPath));
            }
            catch(Exception ex)
            {
                Console.WriteLine($"Cannot read {packageJsonPath}: {ex}");
                return false;
            }

            var existingDevDependencies = new Dictionary<string, string>();
            if (packageJson.TryGetValue("devDependencies", out object devDependencies))
            {
                existingDevDependencies = ((JObject)devDependencies).ToObject<Dictionary<string, string>>();
            }

            bool missingDepAdded = false;

            // For each missing dependency, read its corresponding package.json and retrieve package name and version
            var dependencyEntries = new Dictionary<string, string>();
            foreach (var dependency in missingDependencies)
            {
                var depPackageJsonPath = dependency.WorkingDirectory.Combine(PathTable, "package.json").ToString(PathTable);
                if (!FileUtilities.Exists(depPackageJsonPath))
                {
                    Console.WriteLine($"--- Dependency {depPackageJsonPath} not found. Skipping.");
                    continue;
                }

                var depJson = JsonConvert.DeserializeObject<Dictionary<string, object>>(File.ReadAllText(depPackageJsonPath));
                if (!(depJson.TryGetValue("name", out object outName) && outName is string name))
                {
                    Console.WriteLine($"--- Didn't find an entry for 'name' in {depPackageJsonPath}. Skipping.");
                    continue;
                }

                if (!(depJson.TryGetValue("version", out object outVersion) && outVersion is string version))
                {
                    Console.WriteLine($"--- Didn't find an entry for 'version' in {depPackageJsonPath}. Skipping.");
                    continue;
                }

                // It can be the package has already been fixed but we are running the fixer again with the same log
                if (!existingDevDependencies.ContainsKey(name))
                {
                    dependencyEntries[name] = version;
                    missingDepAdded = true;
                    Console.WriteLine($"--- Adding dependency {name}:{version}");
                }
            }

            if (missingDepAdded)
            {
                // Update devDependencies with all the identified ones and serialize it back
                dependencyEntries.AddRange(existingDevDependencies);

                packageJson["devDependencies"] = JObject.FromObject(dependencyEntries);

                string updatedJson = JsonConvert.SerializeObject(packageJson, Formatting.Indented);

                File.WriteAllText(packageJsonPath, updatedJson);
            }

            Console.WriteLine($"Done with {packageJsonPath}");

            return missingDepAdded;
        }

        /// <inheritdoc/>
        public override void DependencyViolationReported(DependencyViolationEventData data)
        {
            // JavaScript projects are scheduled with allowed undeclared source reads mode on
            // So any undeclared violation manifests as a write on an undeclared source read
            if (data.ViolationType != Scheduler.FileMonitoringViolationAnalyzer.DependencyViolationType.WriteInUndeclaredSourceRead)
            {
                return;
            }
                
            // Put together the project that reads (without a declaration) - as the key - from all the projects that write - as the value -
            var reader = (Process)PipGraph.GetPipFromPipId(data.ViolatorPipId);
            var writer = (Process)PipGraph.GetPipFromPipId(data.RelatedPipId);

            if (m_missingDependencies.TryGetValue(reader, out var writers))
            {
                writers.Add(writer);
            }
            else
            {
                m_missingDependencies[reader] = new HashSet<Process> { writer };
            }
        }
    }
}
