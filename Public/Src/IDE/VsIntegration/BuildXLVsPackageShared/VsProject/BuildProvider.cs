// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Build.Execution;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Build;
using Microsoft.VisualStudio.ProjectSystem.VS.Build;

namespace BuildXL.VsPackage.VsProject
{
    /// <summary>
    /// Proffers the BuildXL BuildProvider which intercepts the SDK-style project build requests
    /// and routes them to the BuildXL build manager.
    /// </summary>
    // Visual Studio will be able to discover this IBuildProvider thanks to this attribute together with
    // the Microsoft.VisualStudio.MefComponent asset type declared in the extension.vsixmanifest.
    [Export(typeof(IBuildProvider))]
    // This IBuildProvider only applies to the .csproj that has the <ProjectCapability Include="BuildXL" /> item.
    [AppliesTo("BuildXL")]
    // 0 is the order of the default build provider implemented by CPS.
    // Using any number greater than 0 means overriding the default.
    [Order(1000, SuppressLowerPriority = true)]
    public sealed class BuildProvider : IBuildProvider
    {
        private static BuildManager? s_buildManager;

        [Import]
        private UnconfiguredProject UnconfiguredProject { get; set; } = null!;

        [Import]
        private ConfiguredProject ConfiguredProject { get; set; } = null!;

        /// <summary>
        /// Sets the BuildManager.
        /// </summary>
        public static void SetBuildManager(BuildManager buildManager)
        {
            s_buildManager = buildManager;
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        public BuildProvider()
        {
            // An instance is created for each of the SDK-style projects.
        }

        /// <summary>
        /// Implements IBuildProvider.SupportsMultiThreadedBuild.
        /// </summary>
        public bool SupportsMultiThreadedBuild => false;

        /// <summary>
        /// Implements IBuildProvider.IsBuildActionSupportedAsync.
        /// </summary>
        public Task<bool> IsBuildActionSupportedAsync(BuildAction buildAction, bool duringQueryStartBuild, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        /// <summary>
        /// Implements IBuildProvider.BuildAsync.
        /// </summary>
        public async Task<IBuildResult> BuildAsync(TextWriter outputWriter, BuildAction buildAction, IImmutableDictionary<string, string>? properties = null, CancellationToken cancellationToken = default)
        {
            bool succ = false;

            if (s_buildManager != null)
            {
                string projectName = UnconfiguredProject.FullPath;

                var projectPropertiesProvider = ConfiguredProject.Services.ProjectPropertiesProvider;
                if (projectPropertiesProvider != null)
                {
                    string buildFilter = await projectPropertiesProvider.GetCommonProperties().GetEvaluatedPropertyValueAsync(Constants.CombinedDominoBuildFilterProp);

                    if (buildFilter == null)
                    {
                        s_buildManager.WriteIncompatibleMessage(Path.GetFileName(projectName));
                    }

                    if (!string.IsNullOrEmpty(projectName) && !string.IsNullOrEmpty(buildFilter))
                    {
                        succ = await s_buildManager.BuildProjectAsync(projectName, buildFilter);
                    }
                }
                else
                {
                    outputWriter.WriteLine("BuildProvider: projectPropertiesProvider is null.");
                }
            }
            else
            {
                outputWriter.WriteLine("BuildProvider: s_buildManager is null.");
            }

            return new BuildXLBuildResult(succ);
        }

        private class BuildXLBuildResult : IBuildResult
        {
            private readonly bool m_succ;

            public BuildResult MSBuildResult => null!;

            public BuildResultCode OverallResult => (m_succ ? BuildResultCode.Success : BuildResultCode.Failure);

            public int Warnings => 0;

            public int Errors => (m_succ ? 0 : 1);

            public BuildXLBuildResult(bool succ)
            {
                m_succ = succ;
            }
        }
    }

    /// <summary>
    /// Implements IBuildUpToDateCheckProvider.
    /// We do not use the default IBuildUpToDateCheckProvider provided by the Common Project System (CPS).
    /// We always return false from IsUpToDateAsync to let BuildXL decide whether to rebuild.
    /// </summary>
    [Export(typeof(IBuildUpToDateCheckProvider))]
    [AppliesTo("BuildXL")]
    [Order(1000, SuppressLowerPriority = true)]
    [ExportMetadata("BeforeDrainCriticalTasks", true)]
    public sealed class BuildUpToDateCheckProvider : IBuildUpToDateCheckProvider
    {
        /// <summary>
        /// Implements IBuildUpToDateCheckProvider.IsUpToDateAsync.
        /// </summary>
        public Task<bool> IsUpToDateAsync(BuildAction buildAction, TextWriter logger, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        /// <summary>
        /// Implements IBuildUpToDateCheckProvider.IsUpToDateCheckEnabledAsync.
        /// </summary>
        public Task<bool> IsUpToDateCheckEnabledAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }
    }
}

#nullable restore
