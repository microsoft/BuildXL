// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using BuildXL.VsPackage;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Build;
using Microsoft.VisualStudio.ProjectSystem.Utilities;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.VsPackage.VsProject
{
    [Export(typeof(BuildManagerMef))]
    internal class BuildManagerMef
    {
        public BuildManager BuildManager { get; set; }
    }

    /// <summary>
    /// Extends the BuildUpToDateCheckProvider and BuildManagerHost for native projects which have 'BuildXLVC' as a project capability.
    /// </summary>
    /// <remarks>
    /// The NativeBuild interepts the build process for native projects such that:
    /// 1.  Adds a new BuildUpToDateChecker which always returns false.
    ///     When this checker is called, we initialize our reflection efforts to wrap and replace the SolutionBuildManagerHost, which is used in the VC project system (VCConfigurationMef)
    /// 2.  Intercepts the Build method in the BuildManagerHost so that we can call BuildManager to add 'OutFilter' directories for native projects.
    /// </remarks>
    [Export(typeof(IBuildUpToDateCheckProvider))]
    [AppliesTo("DominoVC")]
    internal class NativeBuild : IBuildUpToDateCheckProvider, Microsoft.VisualStudio.ProjectSystem.Build.IBuildManagerHost
    {
        // Smallest MsBuild file that can be evaluated with no errors.
        // We use this msbuild file to create BuildResult because the constructor is not public.
        private const string ProjectContents =
@"<?xml version=""1.0"" encoding=""utf-8""?>
<Project ToolsVersion=""12.0"" DefaultTargets=""Build"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <Target Name=""Build"">
  </Target>
</Project>";

        [Import]
        private IProjectLockService ProjectLockService { get; set; }

        [Import("Microsoft.VisualStudio.Project.VisualC.VCProjectEngine.VCConfigurationMef")]
        private object VCConfigurationMef { get; set; }

        [Import]
        private BuildManagerMef BuildManagerHolder { get; set; }

        [Import]
        private ConfiguredProject ConfiguredProject { get; set; }

        private Lazy<Microsoft.VisualStudio.ProjectSystem.Build.IBuildManagerHost> m_solutionBuildManagerHost;

        private static Lazy<BuildResult> m_buildSuccessResult = new Lazy<BuildResult>(() => GetBuildResult("Build"), true);
        private static Lazy<BuildResult> m_buildFailResult = new Lazy<BuildResult>(() => GetBuildResult("Fail"), true);

        private static BuildResult BuildSuccessResult => m_buildSuccessResult.Value;

        private static BuildResult BuildFailResult => m_buildFailResult.Value;

        private static BuildResult GetBuildResult(string target)
        {
            var manager = new Microsoft.Build.Execution.BuildManager();
            Project proj = new Project(XmlReader.Create(new StringReader(ProjectContents)));
            var instance = proj.CreateProjectInstance();
            return manager.Build(new BuildParameters(), new BuildRequestData(instance, new[] { target }));
        }

        private async Task<BuildResult> BuildProjectAsync(CancellationToken cancellationToken)
        {
            using (var readLock = await ProjectLockService.ReadLockAsync(cancellationToken))
            {
                var project = await readLock.GetProjectAsync(ConfiguredProject, cancellationToken);
                var projectName = project.FullPath;

                var buildFilter = project.Xml.Properties.Where(a => a.Name == Constants.DominoBuildFilterProp).Select(a => a.Value).FirstOrDefault();
                if (buildFilter == null)
                {
                    // Some BuildXL native projects do not have the output directory because they do not call linker. It means that the other native projects will build them. That's why, just return 'success' for those projects.
                    return BuildSuccessResult;
                }

                // Building native projects does not use the BuildFilter even if it is given. We just use the spec file filtering for those.
                var specFile = project.Xml.Properties.Where(a => a.Name == Constants.DominoSpecFileProp).Select(a => a.Value).FirstOrDefault();
                if (specFile != null)
                {
                    var result = await BuildManagerHolder.BuildManager.BuildProjectAsync(projectName, SpecUtilities.GenerateSpecFilter(specFile));
                    return result ? BuildSuccessResult : BuildFailResult;
                }

                return BuildFailResult;
            }
        }

        public bool IsCancelable => true;

        public Task<bool> IsUpToDateAsync(BuildAction buildAction, TextWriter logger, CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.FromResult(false);
        }

        public Task<bool> IsUpToDateCheckEnabledAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            // Use reflection to wrap and replace the SolutionBuildManagerHost (implementation of IBuildManagerHost interface),
            // which is used made available to VC projects through internal VCConfigurationMef object.
            if (m_solutionBuildManagerHost == null)
            {
                var type = VCConfigurationMef.GetType();
                var hostProperty = type.GetProperty("SolutionBuildManagerHost", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                var lazyHost = (Lazy<Microsoft.VisualStudio.ProjectSystem.Build.IBuildManagerHost>)hostProperty.GetValue(VCConfigurationMef);
                m_solutionBuildManagerHost = lazyHost;
                var lazyThisHost = new Lazy<Microsoft.VisualStudio.ProjectSystem.Build.IBuildManagerHost>(() => this);
                hostProperty.SetValue(VCConfigurationMef, lazyThisHost);
            }

            return Task.FromResult(false);
        }

        public Task<bool> IsApplicableAsync()
        {
            return m_solutionBuildManagerHost.Value.IsApplicableAsync();
        }

        public IImmutableDictionary<IBuildRequest, Task<BuildResult>> Build(IImmutableSet<IBuildRequest> buildRequests)
        {
            if (buildRequests.Count == 1 && buildRequests.First().BuildRequestData.TargetNames.Contains("Build"))
            {
                var result = BuildProjectAsync(buildRequests.First().CancellationToken);
                return ImmutableDictionary.Create<IBuildRequest, Task<BuildResult>>().Add(buildRequests.First(), result);
            }

            return m_solutionBuildManagerHost.Value.Build(buildRequests);
        }
    }
}
