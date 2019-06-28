// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using BuildXL.VsPackage.VsProject;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
using VSLangProj;
using System.Threading;

namespace BuildXL.VsPackage
{
    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    /// The minimum requirement for a class to be considered a valid package for Visual Studio
    /// is to implement the IVsPackage interface and register itself with the shell.
    /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
    /// to do it: it derives from the Package class that provides the implementation of the
    /// IVsPackage interface and uses the registration attributes defined in the framework to
    /// register itself and its components with the shell.
    /// </summary>
    // This attribute tells the PkgDef creation utility (CreatePkgDef.exe) that this class is
    // a package.
    [SuppressMessage("Microsoft.Naming", "CA1724:TypeNamesShouldNotMatchNamespaces")]
    [PackageRegistration(UseManagedResourcesOnly = true)]

    // This attribute is used to register the information needed to show this package
    // in the Help/About dialog of Visual Studio.
    [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)]
    [ProvideAutoLoad(UIContextGuids.NoSolution, PackageAutoLoadFlags.BackgroundLoad)] // Need to do this so that we can register a project added handler on the global project collection.
    [ProvideAutoLoad(UIContextGuids.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(UIContextGuids.EmptySolution, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExistsAndFullyLoaded_string, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideProjectFactory(typeof(ProjectFlavorFactory), "BuildXL Project", null, null, null, projectTemplatesDirectory: null)]
    [Guid(GuidList.GuidDominoVsPackagePkgString)]
    public sealed class BuildXLVsPackage :
        AsyncPackage,
        IVsSolutionEvents, IVsUpdateSolutionEvents3, IVsSolutionLoadEvents, // Used to hook the solution and project events
        IDisposable
    {
        private const string VsPackagePaneName = "BuildXL VS integration";
        private static readonly Guid VsPackagePaneGuid = new Guid("48a94785-2bf7-4479-85df-352483b5ba83");

        /// <summary>
        /// Main DTE instance
        /// </summary>
        private DTE m_dte;

        /// <summary>
        /// Dictionary that stores handles to all projects info to avoid being garbage collected
        /// </summary>
        private readonly Dictionary<string, ProjectInfo> m_projectInfoDictionary;

        private uint m_solutionEventsCookie;
        private IVsSolution m_vsSolution;

        /// <summary>
        /// Handles to different windows of Visual studio
        /// </summary>
        private IVsOutputWindowPane m_buildOutputPane;
        private ErrorListProvider m_errorListProvider;
        private int m_numErrors;
        private readonly Regex[] m_messageRegexs;
        private IVsOutputWindowPane m_packageOutputPane;

        /// <summary>
        /// Used to locate the project information, given a file name
        /// </summary>
        private IVsUIShellOpenDocument m_shellOpenDocument;

        private IVsTextManager m_textManager;

        /// <summary>
        /// Stores solution event handlers
        /// </summary>
        private IVsSolutionEvents m_vsSolutionEvents;

        /// <summary>
        /// Default constructor of the package.
        /// Inside this method you can place any initialization code that does not require
        /// any Visual Studio service because at this point the package object is created but
        /// not sited yet inside Visual Studio environment. The place to do all the other
        /// initialization is the Initialize method.
        /// </summary>
        public BuildXLVsPackage()
        {
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture, "Entering constructor for: {0}", ToString()));
            m_projectInfoDictionary = new Dictionary<string, ProjectInfo>(StringComparer.OrdinalIgnoreCase);

            // Regular expressions to parse errors or warnings
            m_messageRegexs = new Regex[]
            {
                // Pattern used by CSC and most tools
                // Eg: "d:\src\buildxl\src\Test.BuildXL.Utilities\AnalysisTests.cs(25,13): error CS0103"
                new Regex(@"(?<file>[^\(]+)\((?<linestart>\d+),(?<columnstart>\d+)\)\s*:\s*(?<level>error|warning)(?<message>.*)"),

                // Pattern used by tools that specify start and end tokens, such as cccheck
                // Eg: "d:\src\buildxl2\src\BuildXL.Engine.Commands\CommandEngine.cs(35,17-35,97): error : requires unproven: identifier != null"
                new Regex(@"(?<file>[^\(]+)\((?<linestart>\d+),(?<columnstart>\d+)-(?<lineend>\d+),(?<columnend>\d+)\)\s*:\s*(?<level>error|warning)(?<message>.*)"),
            };
        }

        #region Package Members

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        protected override async System.Threading.Tasks.Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
#if DEBUG
            if (Environment.GetEnvironmentVariable("DominoDebugVsPackageOnStart") == "1")
            {
                System.Diagnostics.Debugger.Launch();
            }
#endif

            Debug.WriteLine(string.Format(CultureInfo.CurrentCulture, "Entering Initialize() of: {0}", ToString()));
            await base.InitializeAsync(cancellationToken, progress);

            // do the rest on the UI thread
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            // Create and register our factory
            RegisterProjectFactory(new ProjectFlavorFactory(this));

            var outWindow = GetGlobalService(typeof(SVsOutputWindow)) as IVsOutputWindow;
            Debug.Assert(outWindow != null, "outWindow != null");

            // Setup a pane for VsPackage messages
            var vsPackagePaneGuid = VsPackagePaneGuid;
            var hResult = outWindow.CreatePane(ref vsPackagePaneGuid, VsPackagePaneName, 1, 0);
            Marshal.ThrowExceptionForHR(hResult);

            hResult = outWindow.GetPane(ref vsPackagePaneGuid, out m_packageOutputPane);
            Marshal.ThrowExceptionForHR(hResult);

            PrintDebugMessage(Strings.DebugMessageDominoPackgeLoading);

            try
            {
                // Modifying events to listen to Add/Remove/Rename items
                m_dte = await GetServiceAsync(typeof(DTE)) as DTE;

                // Setup output pane
                var buildOutputPaneGuid = VSConstants.GUID_BuildOutputWindowPane;
                outWindow.GetPane(ref buildOutputPaneGuid, out m_buildOutputPane);

                // Register solution events (required especially when projects are loaded and unloaded)
                m_vsSolutionEvents = new SolutionEventsHandler(this);
                m_vsSolution = await GetServiceAsync(typeof(SVsSolution)) as IVsSolution;
                ErrorHandler.ThrowOnFailure(m_vsSolution.AdviseSolutionEvents(m_vsSolutionEvents, out m_solutionEventsCookie));
                ErrorHandler.ThrowOnFailure(m_vsSolution.AdviseSolutionEvents(this, out m_solutionEventsCookie));

                // Initializing an error list to be used for showing errors
                m_shellOpenDocument = GetGlobalService(typeof(SVsUIShellOpenDocument)) as IVsUIShellOpenDocument;
                m_textManager = GetGlobalService(typeof(VsTextManagerClass)) as IVsTextManager;
                m_errorListProvider = new ErrorListProvider(this)
                {
                    ProviderName = "Build Accelerator",
                    ProviderGuid = new Guid("5A10E43F-8D1D-4026-98C0-E6B502058901"),
                };

                // Sets the global property to verify whether BuildXL package is installed inside Visual studio or not
                // This helps making the installation of BuildXLVsPackage mandatory
                var globalcollection = Microsoft.Build.Evaluation.ProjectCollection.GlobalProjectCollection;
                globalcollection.SetGlobalProperty(Constants.DominoPackageInstalled, "true");

                // Helps to ensures to force all users to upgrade to the latest version
                SetDominoPackageVersion(globalcollection);

                // Iterate through projects and add events to each project in the solution
                foreach (Project project in GetAllProjects(m_dte.Solution))
                {
                    HandleProject(project);
                }

                PrintDebugMessage(Strings.DebugMessageDominoPackageLoadedSuccessful);
            }
            catch (Exception e)
            {
                PrintDebugMessage(Strings.DebugMessageDominoPackageLoadFailure, e);
                throw;
            }
        }

        private void PrintDebugMessage(string message, params object[] args)
        {
            var hResult = m_packageOutputPane.OutputStringThreadSafe(string.Format(CultureInfo.CurrentCulture, message, args) + Environment.NewLine);
            Marshal.ThrowExceptionForHR(hResult);
        }

        /// <summary>
        /// Retrieves the version from the vsix manifest file and sets the same to the global collection
        /// </summary>
        private void SetDominoPackageVersion(Microsoft.Build.Evaluation.ProjectCollection globalcollection)
        {
            var versionRegex = new Regex("<Identity\\s*"
                                           + "Id=\"(?<GUID>.*)\"\\s*"
                                           + "Version=\"(?<VERSION>.*)\"\\s*"
                                           + "Language=\"en-US\"\\s*"
                                           + "Publisher=\"Microsoft\"\\s*/>");

            string source_extension = null;
            using (var sr = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("BuildXL.VsPackage.source.extension.vsixmanifest")))
            {
                source_extension = sr.ReadToEnd();
            }

            var match = versionRegex.Match(source_extension);
            if (!match.Success)
            {
                OutputMessage(Strings.IncorrectManifestFile);
                return;
            }

            // Ensure that the same version number is mentioned in source.extension.vsixmanifest and Support\BuildXL.Task.targets
            var version = match.Groups["VERSION"].Value.Trim();
            globalcollection.SetGlobalProperty(Constants.DominoPackageVersion, version);
        }

        private static IEnumerable<Project> GetAllProjects(Solution solution)
        {
            foreach (Project project in solution.Projects)
            {
                if (project.Kind == ProjectKinds.vsProjectKindSolutionFolder)
                {
                    foreach (var childProject in GetSubProjects(project))
                    {
                        yield return childProject;
                    }
                }
                else
                {
                    yield return project;
                }
            }
        }

        internal void OutputMessage(string message)
        {
            m_buildOutputPane.OutputString(string.Format(CultureInfo.InvariantCulture, "{0}{1}", message, Environment.NewLine));

            // Check whether this message represents an error or a warning and push to task itemlist accordingly
            ErrorTask task = null;
            string fileName = null;

            Match match = null;
            foreach (Regex regex in m_messageRegexs)
            {
                match = regex.Match(message);
                if (match.Success)
                {
                    break;
                }
            }

            if (match != null && match.Success)
            {
                fileName = match.Groups["file"].Value;
                var lineNumber = int.Parse(match.Groups["linestart"].Value, CultureInfo.InvariantCulture);
                var colNumber = int.Parse(match.Groups["columnstart"].Value, CultureInfo.InvariantCulture);

                var errOrWarning = match.Groups["level"].Value;
                var isAnError = string.Equals(errOrWarning, "error", StringComparison.OrdinalIgnoreCase);
                if (isAnError)
                {
                    m_numErrors++;
                }

                task = new ErrorTask
                {
                    Document = fileName,
                    Line = lineNumber - 1,
                    Column = colNumber - 1,
                    Text = match.Groups["message"].Value,
                    Priority = isAnError ? TaskPriority.High : TaskPriority.Normal,
                    ErrorCategory = isAnError ? TaskErrorCategory.Error : TaskErrorCategory.Warning,
                    Category = TaskCategory.BuildCompile,
                };
            }
            else
            {
                // If the standard pattern is not followed for errors
                if (message.StartsWith("error ", StringComparison.OrdinalIgnoreCase)
                    || message.StartsWith("error:", StringComparison.OrdinalIgnoreCase))
                {
                    task = new ErrorTask
                    {
                        Text = message,
                        Priority = TaskPriority.High,
                        ErrorCategory = TaskErrorCategory.Error,
                        Category = TaskCategory.BuildCompile,
                    };
                }
            }

            // Add the task (if not null) to error list
            if (task != null)
            {
                if (!string.IsNullOrEmpty(fileName))
                {
                    // populating the project hierarchy to show the project name in the error list window
                    IVsUIHierarchy vsHierarchy;
                    uint itemId;
                    Microsoft.VisualStudio.OLE.Interop.IServiceProvider serviceProvider;
                    var docInProj = 0;

                    var docOpenResult = m_shellOpenDocument.IsDocumentInAProject(
                        fileName,
                        out vsHierarchy,
                        out itemId,
                        out serviceProvider,
                        out docInProj);
                    if (docInProj != 0 && docOpenResult == VSConstants.S_OK)
                    {
                        task.HierarchyItem = vsHierarchy;
                    }
                }

                // Adding navigation handler
                task.Navigate += NavigateTo;
                m_errorListProvider.Tasks.Add(task);
            }
        }

        /// <summary>
        /// Navigate to the file, line and column reported in the task
        /// </summary>
        /// <param name="sender">The Task to navigate to</param>
        /// <param name="arguments">The event arguments</param>
        private void NavigateTo(object sender, EventArgs arguments)
        {
            var task = sender as Task;
            if (task == null || string.IsNullOrEmpty(task.Document))
            {
                // nothing to navigate to
                return;
            }

            IVsWindowFrame frame;
            Microsoft.VisualStudio.OLE.Interop.IServiceProvider sp;
            IVsUIHierarchy hier;
            uint itemid;
            Guid logicalView = VSConstants.LOGVIEWID_Code;

            if (ErrorHandler.Failed(m_shellOpenDocument.OpenDocumentViaProject(task.Document, ref logicalView, out sp, out hier, out itemid, out frame))
                || frame == null)
            {
                return;
            }

            object docData;
            frame.GetProperty((int)__VSFPROPID.VSFPROPID_DocData, out docData);

            // Get the VsTextBuffer
            var buffer = docData as VsTextBuffer;
            if (buffer == null)
            {
                var bufferProvider = docData as IVsTextBufferProvider;
                if (bufferProvider != null)
                {
                    IVsTextLines lines;
                    ErrorHandler.ThrowOnFailure(bufferProvider.GetTextBuffer(out lines));
                    buffer = lines as VsTextBuffer;
                    if (buffer == null)
                    {
                        return;
                    }
                }
            }

            m_textManager.NavigateToLineAndColumn(buffer, ref logicalView, task.Line, task.Column, task.Line, task.Column);
        }

        /// <summary>
        /// Handles a single project
        /// </summary>
        /// <param name="project">The project that needs to be handled</param>
        private void HandleProject(Project project)
        {
            IVsHierarchy projectHierarchy;
            if (m_vsSolution.GetProjectOfUniqueName(project.UniqueName, out projectHierarchy) != VSConstants.S_OK)
            {
                return;
            }

            Debug.WriteLine(string.Format(CultureInfo.CurrentCulture, "Registering events for: {0}", project.UniqueName));
            RegisterEventsForProject(project, projectHierarchy);
        }

        /// <summary>
        /// Gets all sub-projects in a given project folder
        /// </summary>
        /// <param name="projectFolder">The project folder whose sub-folders need to be iterated</param>
        /// <returns>Returns list of subfolders</returns>
        private static IEnumerable<Project> GetSubProjects(Project projectFolder)
        {
            for (var counter = 1; counter <= projectFolder.ProjectItems.Count; counter++)
            {
                var subProject = projectFolder.ProjectItems.Item(counter).SubProject;
                if (subProject == null)
                {
                    continue;
                }

                // If this is another solution folder, do a recursive call, otherwise add
                if (subProject.Kind == ProjectKinds.vsProjectKindSolutionFolder)
                {
                    foreach (var childProject in GetSubProjects(subProject))
                    {
                        yield return childProject;
                    }
                }
                else
                {
                    yield return subProject;
                }
            }
        }

        /// <summary>
        /// Registers events for a given project
        /// </summary>
        /// <param name="project">The project instance on which events need to be registered</param>
        /// <param name="projectHierarchy">The hierarchy of that project instance</param>
        internal void RegisterEventsForProject(Project project, IVsHierarchy projectHierarchy)
        {
            // Check whether the project type is supported
            if (!IsSupported(project))
            {
                return;
            }

            uint cookie;
            var hierarchyEventsHandler = new HierarchyEventsHandler(projectHierarchy);
            ErrorHandler.ThrowOnFailure(projectHierarchy.AdviseHierarchyEvents(hierarchyEventsHandler, out cookie));

            var vsproject = project.Object as VSProject;
            if (vsproject == null)
            {
                return;
            }

            var pInfo = new ProjectInfo
            {
                Cookie = cookie,
                HierarchyEventsHandler = hierarchyEventsHandler,
                ReferencesEvents = vsproject.Events.ReferencesEvents,
            };
            pInfo.ReferencesEvents.ReferenceAdded += ReferencesEventsHandler.ReferenceAdded;
            pInfo.ReferencesEvents.ReferenceRemoved += ReferencesEventsHandler.ReferenceRemoved;

            m_projectInfoDictionary[project.UniqueName] = pInfo;
        }

        /// <summary>
        /// Deregisters events for a project, in case the project is closed
        /// </summary>
        /// <param name="projectHierarchy">The project hierarchy instance</param>
        internal void DeRegisterEventsForProject(IVsHierarchy projectHierarchy)
        {
            // Get the project info of the object
            object proj;
            var result = projectHierarchy.GetProperty(
                VSConstants.VSITEMID_ROOT,
                (int)__VSHPROPID.VSHPROPID_ExtObject,
                out proj);

            if (result != VSConstants.S_OK)
            {
                return;
            }

            var project = proj as Project;

            if (project == null
                || project.Properties == null
                || !m_projectInfoDictionary.ContainsKey(project.UniqueName))
            {
                return;
            }

            var pInfo = m_projectInfoDictionary[project.UniqueName];
            pInfo.ReferencesEvents.ReferenceAdded -= ReferencesEventsHandler.ReferenceAdded;
            pInfo.ReferencesEvents.ReferenceRemoved -= ReferencesEventsHandler.ReferenceRemoved;

            // Unadvise hierarchy events
            ErrorHandler.ThrowOnFailure(projectHierarchy.UnadviseHierarchyEvents(pInfo.Cookie));

            m_projectInfoDictionary.Remove(project.UniqueName);
        }

        /// <summary>
        /// Checks whether the project type is supported or not
        /// </summary>
        /// <param name="project">The project instance</param>
        /// <returns>Returns whether the project is supported or not</returns>
        private static bool IsSupported(Project project)
        {
            var kind = project.Kind;

            return kind.Equals(Constants.CsProjGuid, StringComparison.OrdinalIgnoreCase)
                || kind.Equals(Constants.VcxProjGuid, StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        /// <summary>
        /// Disposes members explicitly
        /// </summary>
        public void Dispose()
        {
            if (m_errorListProvider != null)
            {
                m_errorListProvider.Dispose();
            }
        }

        private SolutionConfiguration2 GetEnvironment()
        {
            var build = m_dte.Solution.SolutionBuild;
            if (build == null)
            {
                return null;
            }

            return build.ActiveConfiguration as SolutionConfiguration2;
        }

        /// <summary>
        /// Gets the current build configuration (e.g., release) as set in the solution build manager
        /// </summary>
        private string Configuration
        {
            get
            {
                var activeEnvironment = GetEnvironment();
                return activeEnvironment == null ? null : activeEnvironment.Name;
            }
        }

        #region Vs Solution Events

        /// <inheritdoc />
        public int OnAfterCloseSolution(object pUnkReserved)
        {
            return VSConstants.E_NOTIMPL;
        }

        /// <inheritdoc />
        public int OnAfterLoadProject(IVsHierarchy pStubHierarchy, IVsHierarchy pRealHierarchy)
        {
            return VSConstants.E_NOTIMPL;
        }

        /// <inheritdoc />
        public int OnAfterOpenProject(IVsHierarchy pHierarchy, int fAdded)
        {
            return VSConstants.E_NOTIMPL;
        }

        /// <inheritdoc />
        public int OnAfterOpenSolution(object pUnkReserved, int fNewSolution)
        {
            return VSConstants.E_NOTIMPL;
        }

        /// <inheritdoc />
        public int OnBeforeCloseProject(IVsHierarchy pHierarchy, int fRemoved)
        {
            return VSConstants.E_NOTIMPL;
        }

        /// <inheritdoc />
        public int OnBeforeCloseSolution(object pUnkReserved)
        {
            return VSConstants.E_NOTIMPL;
        }

        /// <inheritdoc />
        public int OnBeforeUnloadProject(IVsHierarchy pRealHierarchy, IVsHierarchy pStubHierarchy)
        {
            return VSConstants.E_NOTIMPL;
        }

        /// <inheritdoc />
        public int OnQueryCloseProject(IVsHierarchy pHierarchy, int fRemoving, ref int pfCancel)
        {
            return VSConstants.E_NOTIMPL;
        }

        /// <inheritdoc />
        public int OnQueryCloseSolution(object pUnkReserved, ref int pfCancel)
        {
            return VSConstants.E_NOTIMPL;
        }

        /// <inheritdoc />
        public int OnQueryUnloadProject(IVsHierarchy pRealHierarchy, ref int pfCancel)
        {
            return VSConstants.E_NOTIMPL;
        }

        /// <inheritdoc />
        public int OnAfterActiveSolutionCfgChange(IVsCfg pOldActiveSlnCfg, IVsCfg pNewActiveSlnCfg)
        {
            return VSConstants.E_NOTIMPL;
        }

        /// <inheritdoc />
        public int OnBeforeActiveSolutionCfgChange(IVsCfg pOldActiveSlnCfg, IVsCfg pNewActiveSlnCfg)
        {
            return VSConstants.E_NOTIMPL;
        }

        /// <inheritdoc />
        public int OnAfterBackgroundSolutionLoadComplete()
        {
            return VSConstants.E_NOTIMPL;
        }

        /// <inheritdoc />
        public int OnAfterLoadProjectBatch(bool fIsBackgroundIdleBatch)
        {
            return VSConstants.E_NOTIMPL;
        }

        /// <inheritdoc />
        public int OnBeforeBackgroundSolutionLoadBegins()
        {
            return VSConstants.E_NOTIMPL;
        }

        /// <inheritdoc />
        public int OnBeforeLoadProjectBatch(bool fIsBackgroundIdleBatch)
        {
            return VSConstants.E_NOTIMPL;
        }

        /// <inheritdoc />
        public int OnBeforeOpenSolution(string pszSolutionFilename)
        {
            return VSConstants.S_OK;
        }

        /// <inheritdoc />
        public int OnQueryBackgroundLoadProjectBatch(out bool pfShouldDelayLoadToNextIdle)
        {
            pfShouldDelayLoadToNextIdle = false;
            return VSConstants.S_OK;
        }

        #endregion Vs Solution Events
    }

    /// <summary>
    /// Stores the information about projects, primarily to avoid being garbage collected
    /// </summary>
    internal sealed class ProjectInfo
    {
        internal ReferencesEvents ReferencesEvents { get; set; }

        internal uint Cookie { get; set; }

        internal HierarchyEventsHandler HierarchyEventsHandler { get; set; }
    }
}
