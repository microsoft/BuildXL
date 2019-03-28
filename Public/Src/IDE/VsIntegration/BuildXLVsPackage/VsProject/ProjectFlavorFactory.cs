// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using BuildXL.VsPackage;
using EnvDTE;
using Microsoft.Internal.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Events;
using Microsoft.VisualStudio.Shell.Flavor;
using Microsoft.VisualStudio.Shell.Interop;
using SolutionEvents = Microsoft.VisualStudio.Shell.Events.SolutionEvents;

namespace BuildXL.VsPackage.VsProject
{
    /// <summary>
    /// Creates the BuildXL Project Flavor which intercepts build requests and calls BuildXL instead.
    /// </summary>
    /// <remarks>
    /// The BuildXL project flavor performs these functions:
    /// 1.  Removes all dependencies from projects. This keeps the solution build manager from building dependency first and instead just calls the projects that actually need to build.
    ///     NOTE: This may not be explicitly necessary now that there is a means to check whether the build has more items to build (via IVsSolutionBuildManagerPrivate)
    /// 2.  Intercepts build requests and calls registers the 'project' to build.
    ///     Projects/spec files are built by building all the files under the given output directory.
    /// 3.  Understands if there are more projects to build and only (builds when the solution build manager is requesting the last project. This ensures all projects are registered with
    ///     the build before BuildXL is invoked because BuildXL needs to know the full set of things to build upfront.
    /// </remarks>
    [Guid(ProjectFlavorGuid)]
    public class ProjectFlavorFactory : FlavoredProjectFactoryBase, IVsUpdateSolutionEvents2, IBuildManagerHost
    {
        /// <nodoc />
        public const string ProjectFlavorGuid = "DABA23A1-650F-4EAB-AC72-A2AF90E10E37";
        private IServiceProvider m_serviceProvider;
        private readonly BuildManager m_buildManager;

        private readonly Action<string> m_traceBuildXLMessage;
        private readonly Action<string> m_buildMessage;

        /// <nodoc />
        public const string OutputPaneGuidString = "6984449f-4091-4eae-93ed-47e96f5a6ea0";
        private static readonly Guid DebugOutputPaneGuid = new Guid(OutputPaneGuidString);
        private readonly IVsOutputWindowPane m_dominoBuildPane;
        private readonly IVsSolutionBuildManagerPrivate m_solutionBuildManagerPrivate;
        private readonly TaskScheduler m_idleTaskScheduler;

        /// <nodoc />
        public ProjectFlavorFactory(BuildXLVsPackage package)
        {
            m_serviceProvider = package;
            m_buildMessage = package.OutputMessage;
            var outputWindow = m_serviceProvider.GetService(typeof(SVsOutputWindow)) as IVsOutputWindow;
            var sbm = m_serviceProvider.GetService(typeof(SVsSolutionBuildManager)) as IVsSolutionBuildManager;
            m_solutionBuildManagerPrivate = (IVsSolutionBuildManagerPrivate)sbm;
            uint cookie;
            sbm.AdviseUpdateSolutionEvents(this, out cookie);
            var guid = DebugOutputPaneGuid;

            var componentModel = (IComponentModel)m_serviceProvider.GetService(typeof(SComponentModel));
            var buildManagerHolder = componentModel.GetService<BuildManagerMef>();

            var taskSchedulerService = (IVsTaskSchedulerService2)m_serviceProvider.GetService(typeof(SVsTaskSchedulerService));
            m_idleTaskScheduler = (TaskScheduler)taskSchedulerService.GetTaskScheduler((uint)VsTaskRunContext.UIThreadBackgroundPriority);
            m_buildManager = new BuildManager(this);
            buildManagerHolder.BuildManager = m_buildManager;

            IVsOutputWindowPane dominoDebugPane;
            outputWindow.GetPane(ref guid, out dominoDebugPane);

            if (dominoDebugPane == null)
            {
                outputWindow.CreatePane(ref guid, "BuildXL Debug", 1, 1);
                outputWindow.GetPane(ref guid, out dominoDebugPane);
            }

            if (dominoDebugPane != null)
            {
                m_traceBuildXLMessage = (message) => dominoDebugPane.OutputStringThreadSafe(message + "\n");
            }
            else
            {
                m_traceBuildXLMessage = (message) => Debug.WriteLine(message);
            }

            guid = VSConstants.OutputWindowPaneGuid.BuildOutputPane_guid;
            outputWindow.GetPane(ref guid, out m_dominoBuildPane);

            m_buildMessage = (message) => m_dominoBuildPane.OutputStringThreadSafe(message + "\n");

            SolutionEvents.OnBeforeOpenSolution += OnBeforeOpenSolution;
            SolutionEvents.OnAfterCloseSolution += OnAfterCloseSolution;

            DTE dte = (DTE)m_serviceProvider.GetService(typeof(SDTE));
            var solutionFileName = dte.Solution?.FileName;
            if (solutionFileName != null)
            {
                OnOpenSolution(solutionFileName);
            }
        }

        private void OnAfterCloseSolution(object sender, EventArgs e)
        {
            m_buildManager.SetIdeFolderPath(null);
        }

        private void OnBeforeOpenSolution(object sender, BeforeOpenSolutionEventArgs e)
        {
            var solutionFilename = e.SolutionFilename;
            OnOpenSolution(solutionFilename);
        }

        private void OnOpenSolution(string solutionFilename)
        {
            string ideFolderPath = null;
            if (!string.IsNullOrEmpty(solutionFilename))
            {
                ideFolderPath = Path.GetDirectoryName(solutionFilename);
            }

            m_buildManager.SetIdeFolderPath(ideFolderPath);
        }

        /// <inheritdoc />
        protected override object PreCreateForOuter(IntPtr outerProjectIUnknown)
        {
            return new ProjectFlavor(m_serviceProvider, m_buildManager, m_traceBuildXLMessage);
        }

        /// <nodoc />
        public int UpdateSolution_Begin(ref int pfCancelUpdate)
        {
            m_buildManager.StartBuild(new BuildStartArguments());
            PrintCalled();
            return VSConstants.S_OK;
        }

        /// <nodoc />
        public int UpdateSolution_Done(int fSucceeded, int fModified, int fCancelCommand)
        {
            m_buildManager.EndBuild();
            return VSConstants.S_OK;
        }

        /// <nodoc />
        public int UpdateSolution_StartUpdate(ref int pfCancelUpdate)
        {
            m_buildManager.StartBuild(new BuildStartArguments());
            return VSConstants.S_OK;
        }

        /// <nodoc />
        public int UpdateSolution_Cancel()
        {
            m_buildManager.CancelBuild();
            return VSConstants.S_OK;
        }

        /// <nodoc />
        public int OnActiveProjectCfgChange(IVsHierarchy pIVsHierarchy)
        {
            return VSConstants.E_NOTIMPL;
        }

        private static void PrintCalled([CallerMemberName] string memberName = null)
        {
            // m_traceBuildXLMessage($"BuildXLProjectConfiguration: {memberName}\n");
        }

        /// <nodoc />
        public int UpdateProjectCfg_Begin(IVsHierarchy pHierProj, IVsCfg pCfgProj, IVsCfg pCfgSln, uint dwAction, ref int pfCancel)
        {
            return VSConstants.E_NOTIMPL;
        }

        /// <nodoc />
        public int UpdateProjectCfg_Done(IVsHierarchy pHierProj, IVsCfg pCfgProj, IVsCfg pCfgSln, uint dwAction, int fSuccess, int fCancel)
        {
            return VSConstants.E_NOTIMPL;
        }

        void IBuildManagerHost.WriteBuildMessage(string message)
        {
            m_buildMessage(message);
        }

        bool IBuildManagerHost.HasMoreProjects()
        {
            IVsHierarchy next;
            m_solutionBuildManagerPrivate.GetNextBuildItemForUIThread(out next);
            return next != null;
        }
    }
}
