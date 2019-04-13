// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.FrontEnd.MsBuild.Serialization;
using Xunit;

namespace Test.ProjectGraphBuilder.Utilities
{
    /// <summary>
    /// Utility class for building and validating MsBuild projects
    /// </summary>
    public class MsBuildProjectBuilder
    {
        private readonly string m_outputDirectoryRoot;

        /// <nodoc/>
        public MsBuildProjectBuilder(string outputDirectoryRoot)
        {
            Contract.Requires(!string.IsNullOrEmpty(outputDirectoryRoot));
            m_outputDirectoryRoot = outputDirectoryRoot;
        }

        /// <summary>
        /// Translates P2P reference chains into MsBuild projects.
        /// </summary>
        /// <remarks>
        /// A chain is expected to have a shape like "P1 -> P2 -> P3", the arrow meaning a project reference
        /// The entry point project is flagged with square brackets, e.g.: [P1] -> P2 -> P3
        /// Multiple chains can be specified, allowing for arbitrary graphs. That means projects can show up multiple times in 
        /// different chains. For example, P1 -> P2 and P1 -> P3 represents a project graph like:
        /// 
        /// P1 --> P2
        /// |
        /// +----> P3
        /// </remarks>
        /// <returns>
        /// Path to the project entry point
        /// </returns>
        public string WriteProjectsWithReferences(params string[] projectChains)
        {
            var projectsAndReferences = ProcessProjectChains(projectChains, out var entryPoint);

            // Create a unique sub-directory to hold all the projects
            string outputDirectory = Path.Combine(m_outputDirectoryRoot, Guid.NewGuid().ToString());
            Directory.CreateDirectory(outputDirectory);

            // Write all generated projects to disk
            foreach (var kvp in projectsAndReferences)
            {
                var projectContent = BuildProject(kvp.Value);
                var projectFile = Path.Combine(outputDirectory, kvp.Key);
                File.WriteAllText(projectFile, projectContent);
            }

            return Path.Combine(outputDirectory, entryPoint);
        }

        /// <summary>
        /// Writes a collection of projects to disk, where the name and content of the project is already computed
        /// </summary>
        /// <remarks>
        /// The first project of the collection is assumed to be the entry point
        /// </remarks>
        public string WriteProjectsWithReferences(params (string projectName, string projectContent)[] projects)
        {
            // Create a unique sub-directory to hold all the projects
            string outputDirectory = Path.Combine(m_outputDirectoryRoot, Guid.NewGuid().ToString());
            Directory.CreateDirectory(outputDirectory);

            // Write all generated projects to disk
            foreach (var kvp in projects)
            {
                var projectFile = Path.Combine(outputDirectory, kvp.projectName);
                File.WriteAllText(projectFile, kvp.projectContent);
            }

            return Path.Combine(outputDirectory, projects[0].projectName);
        }


        /// <summary>
        /// Validates that a <param name="projectGraph"/> is contained by a set of <see cref="projectChains"/>. If <param name="assertEqual"/> is specified,
        /// the graph must match exactly (not just be a sub-graph).
        /// </summary>
        public void ValidateGraphIsSubgraphOfChains(ProjectGraphWithPredictions<string> projectGraph, bool assertEqual, params string[] projectChains)
        {
            var projectsAndReferences = ProcessProjectChains(projectChains, out var entryPoint);

            if (assertEqual)
            {
                // Number of projects should be equal
                Assert.True(projectGraph.ProjectNodes.Length == projectsAndReferences.Keys.Count, $"Expected {projectGraph.ProjectNodes.Length} nodes but found '{projectsAndReferences.Keys.Count}'");
            }

            foreach (ProjectWithPredictions<string> node in projectGraph.ProjectNodes)
            {
                // We should be able to find the corresponding project in the chains
                string nodeName = Path.GetFileName(node.FullPath);
                Assert.True(projectsAndReferences.ContainsKey(nodeName));

                // And its references should match
                IEnumerable<string> nodeReferenceNames = node.ProjectReferences.Select(nodeReference => Path.GetFileName(nodeReference.FullPath));
                if (!projectsAndReferences[nodeName].SetEquals(nodeReferenceNames))
                {
                    var difference = new HashSet<string>(projectsAndReferences[nodeName]);
                    difference.SymmetricExceptWith(nodeReferenceNames);
                    
                    Assert.True(false, $"Expected same set of references but these elements are not in the intersection of them: {string.Join(",", difference)}");
                }
            }
        }

        private static Dictionary<string, HashSet<string>> ProcessProjectChains(string[] projectChains, out string projectEntryPoint)
        {
            projectEntryPoint = null;

            // Process all project chains and aggregate references
            Dictionary<string, HashSet<string>> projectsAndReferences = new Dictionary<string, HashSet<string>>();
            foreach (var projectChain in projectChains)
            {
                var projects = projectChain.Split(new[] { "->" }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < projects.Length; i++)
                {
                    var project = projects[i].Trim(' ');
                    if (!projectsAndReferences.ContainsKey(project))
                    {
                        if (project.StartsWith("[") && project.EndsWith("]"))
                        {
                            // There should be just one defined entry point
                            Contract.Assert(projectEntryPoint == null, "At most one entry point needs to be defined");
                            project = project.Trim('[', ']');
                            projectEntryPoint = project;
                        }
                        projectsAndReferences.Add(project, new HashSet<string>());
                    }

                    if (i != projects.Length - 1)
                    {
                        var references = projectsAndReferences[project];
                        references.Add(projects[i + 1].Trim(' '));
                    }
                }
            }

            // There should be at least one entry point defined
            Contract.Assert(projectEntryPoint != null, "At least one entry point needs to be defined");

            return projectsAndReferences;
        }

        private static string BuildProject(IEnumerable<string> projectReferences)
        {
            return
$@"<?xml version=""1.0"" encoding=""utf-8""?>
<Project xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
   <ItemGroup>
      {string.Join(Environment.NewLine, projectReferences.Select(reference => CreateReference(reference)))}
   </ItemGroup>
</Project>";
        }

        private static string CreateReference(string dependency)
        {
            return
$@"<ProjectReference Include=""{dependency}"">
      <Name>{dependency}</Name>
</ProjectReference>
";
        }
    }
}
