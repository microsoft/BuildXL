// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Qualifier;

namespace BuildXL.Ide.Generator
{
    internal sealed class Context
    {
        internal static HashSet<string> HandledItemTypes = new HashSet<string>()
                                                  {
                                                      "ProjectReference",
                                                      "Reference",
                                                      "Compile",
                                                      "EmbeddedResource",
                                                      "BootstrapperPackage",
                                                  };

        /// <summary>
        /// ".resx"
        /// </summary>
        internal readonly PathAtom ResxExtensionName;

        /// <summary>
        /// ".cs"
        /// </summary>
        internal readonly PathAtom CsExtensionName;

        /// <summary>
        /// ".dll"
        /// </summary>
        internal readonly PathAtom DllExtensionName;

        /// <summary>
        /// ".resources"
        /// </summary>
        internal readonly PathAtom ResourcesExtensionName;

        /// <summary>
        /// "csc.exe"
        /// </summary>
        internal readonly PathAtom CscExeName;

        /// <summary>
        /// "cl.exe"
        /// </summary>
        internal readonly PathAtom ClExeName;

        /// <summary>
        /// "ResGen.exe"
        /// </summary>
        internal readonly PathAtom ResgenExeName;

        /// <summary>
        /// "Link.exe"
        /// </summary>
        internal readonly PathAtom LinkExeName;

        /// <summary>
        /// "vstest.console.exe"
        /// </summary>
        internal readonly PathAtom VsTestExeName;

        internal readonly PipGraph PipGraph;
        internal readonly IReadonlyDirectedGraph ScheduledGraph;
        internal readonly PathTable PathTable;
        internal readonly StringTable StringTable;
        internal readonly SymbolTable SymbolTable;
        internal readonly QualifierTable QualifierTable;

        internal readonly AbsolutePath ProjectsRoot;
        internal readonly AbsolutePath EnlistmentRoot;
        internal readonly string EnlistmentRootStr;

        internal readonly AbsolutePath SolutionRoot;
        internal readonly string SolutionRootStr;

        internal readonly string SolutionFilePathStr;
        internal readonly string DotSettingsPathStr;
        internal readonly string ConfigFilePathStr;

        internal readonly bool CanWriteToSrc;

        internal readonly StringId AssemblyDeploymentTag;
        internal readonly StringId TestDeploymentTag;
        
        public Context(
            PipExecutionContext pipContext,
            PipGraph pipGraph,
            IReadonlyDirectedGraph scheduledGraph,
            AbsolutePath configFilePath,
            IIdeConfiguration ideConfig)
        {
            Contract.Requires(pipGraph != null);
            Contract.Requires(scheduledGraph != null);
            Contract.Requires(configFilePath.IsValid);
            Contract.Requires(ideConfig.SolutionRoot.IsValid);
            Contract.Requires(ideConfig.SolutionName.IsValid);
            Contract.Requires(ideConfig.IsEnabled);
            Contract.Requires(ideConfig.IsNewEnabled);

            PipGraph = pipGraph;
            ScheduledGraph = scheduledGraph;
            StringTable = pipContext.StringTable;
            PathTable = pipContext.PathTable;
            SymbolTable = pipContext.SymbolTable;
            QualifierTable = pipContext.QualifierTable;

            DotSettingsPathStr = ideConfig.DotSettingsFile.IsValid ? ideConfig.DotSettingsFile.ToString(PathTable) : null;
            ConfigFilePathStr = configFilePath.ToString(PathTable);

            EnlistmentRoot = configFilePath.GetParent(PathTable);
            EnlistmentRootStr = EnlistmentRoot.ToString(PathTable);

            SolutionRoot = ideConfig.SolutionRoot;
            SolutionRootStr = SolutionRoot.ToString(PathTable);

            SolutionFilePathStr = IdeGenerator.GetSolutionPath(ideConfig, PathTable).ToString(PathTable);

            CanWriteToSrc = ideConfig.CanWriteToSrc ?? false;
            ProjectsRoot = CanWriteToSrc
                    ? EnlistmentRoot
                    : SolutionRoot.Combine(PathTable, PathAtom.Create(PathTable.StringTable, "Projects"));

            ResxExtensionName = PathAtom.Create(StringTable, ".resx");
            CscExeName = PathAtom.Create(StringTable, "csc.exe");
            ResgenExeName = PathAtom.Create(StringTable, "ResGen.exe");
            ResourcesExtensionName = PathAtom.Create(StringTable, ".resources");
            CsExtensionName = PathAtom.Create(StringTable, ".cs");
            DllExtensionName = PathAtom.Create(StringTable, ".dll");
            ClExeName = PathAtom.Create(StringTable, "cl.exe");
            LinkExeName = PathAtom.Create(StringTable, "Link.exe");
            VsTestExeName = PathAtom.Create(StringTable, "vstest.console.exe");
            AssemblyDeploymentTag = StringId.Create(StringTable, "assemblyDeployment");
            TestDeploymentTag = StringId.Create(StringTable, "testDeployment");
        }

        public RelativePath GetRelativePath(AbsolutePath specFilePath)
        {
            RelativePath relativePath;
            if (!EnlistmentRoot.TryGetRelative(PathTable, specFilePath, out relativePath))
            {
                throw new BuildXLException("Spec file is not under the enlistment root");
            }

            return relativePath;
        }

        public IEnumerable<AbsolutePath> EnumeratePipGraphFilesUnderDirectory(AbsolutePath directory)
        {
            foreach (var path in PipGraph.EnumerateImmediateChildPaths(directory))
            {
                var latestFile = PipGraph.TryGetLatestFileArtifactForPath(path);
                if (latestFile.IsValid)
                {
                    if (latestFile.IsSourceFile)
                    {
                        yield return path;
                    }

                    continue;
                }

                foreach (var childPath in EnumeratePipGraphFilesUnderDirectory(path))
                {
                    yield return childPath;
                }
            }
        }

        /// <summary>
        /// Gets the command line arguments for the process.
        /// </summary>
        public PipData GetArgumentsDataFromProcess(Process process)
        {
            PipData arguments = process.Arguments;
            if (process.ResponseFile.IsValid)
            {
                var responseFileData = process.ResponseFileData;
                PipDataBuilder pipDataBuilder = new PipDataBuilder(StringTable);

                // Add all the arguments from the command line excluding the response file (the last fragment)
                foreach (var fragment in process.Arguments.Take(process.Arguments.FragmentCount - 1).Concat(responseFileData))
                {
                    Contract.Assume(fragment.FragmentType != PipFragmentType.Invalid);

                    pipDataBuilder.Add(fragment);
                }

                arguments = pipDataBuilder.ToPipData(arguments.FragmentSeparator, arguments.FragmentEscaping);
            }

            return arguments;
        }
    }
}
