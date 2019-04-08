// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BuildXL.VsPackage;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace BuildXL.VsPackage.VsProject
{
    /// <summary>
    /// Proffers the BuildXL BuildableProjectConfig which intercepts build requests and routes
    /// them to the BuildXL build manager.
    /// </summary>
    public sealed class BuildProjectConfiguration : IVsProjectFlavorCfg
    {
        private BuildableProjectCfg m_buildableProjectCfg;
        private readonly IVsProjectFlavorCfg m_innerConfiguration;
        private readonly IVsCfg m_baseProjectConfiguration;
        private readonly BuildManager m_buildManager;
        private readonly Action<string> m_traceBuildXLMessage;
        private readonly IVsBuildPropertyStorage m_buildPropertyStorage;
        private readonly string m_projectName;

        /// <nodoc />
        public BuildProjectConfiguration(
            IVsCfg baseProjectConfiguration,
            IVsProjectFlavorCfg innerConfiguration,
            BuildManager buildManager,
            Action<string> traceBuildXLMessage,
            IVsBuildPropertyStorage buildPropertyStorage,
            string projectName)
        {
            m_baseProjectConfiguration = baseProjectConfiguration;
            m_innerConfiguration = innerConfiguration;
            m_buildManager = buildManager;
            m_traceBuildXLMessage = traceBuildXLMessage;
            m_buildPropertyStorage = buildPropertyStorage;
            m_projectName = projectName;
        }

        /// <nodoc />
        public int Close()
        {
            return m_innerConfiguration?.Close() ?? VSConstants.S_OK;
        }

        int IVsProjectFlavorCfg.get_CfgType(ref Guid iidCfg, out IntPtr ppCfg)
        {
            ppCfg = IntPtr.Zero;
            if (iidCfg == typeof(IVsBuildableProjectCfg).GUID)
            {
                if (m_buildableProjectCfg == null)
                {
                    IVsBuildableProjectCfg innerBuildableCfg;
                    int hr = GetBuildableProjectCfg(out innerBuildableCfg);
                    m_buildableProjectCfg = new BuildableProjectCfg(innerBuildableCfg, m_buildManager, this);
                }

                ppCfg = Marshal.GetComInterfaceForObject(m_buildableProjectCfg, typeof(IVsBuildableProjectCfg));
                return VSConstants.S_OK;
            }

            return m_innerConfiguration?.get_CfgType(ref iidCfg, out ppCfg) ?? VSConstants.S_OK;
        }

        /// <nodoc />
        public int GetBuildableProjectCfg(out IVsBuildableProjectCfg pb)
        {
            if (m_baseProjectConfiguration != null && m_baseProjectConfiguration is IVsProjectCfg)
            {
                return ((IVsProjectCfg)m_baseProjectConfiguration).get_BuildableProjectCfg(out pb);
            }

            pb = null;
            return VSConstants.E_NOTIMPL;
        }

        private sealed class BuildableProjectCfg : IVsBuildableProjectCfg2, IVsBuildableProjectCfg
        {
            private readonly IVsBuildableProjectCfg m_innerCfg;
            private readonly IVsBuildableProjectCfg2 m_innerCfg2;
            private readonly BuildManager m_buildManager;
            private EventSinkCollection m_callbacks = new EventSinkCollection();
            private BuildProjectConfiguration m_buildProjectConfiguration;

            public BuildableProjectCfg(
                IVsBuildableProjectCfg innerBuildableCfg,
                BuildManager buildManager,
                BuildProjectConfiguration buildProjectConfiguration)
            {
                m_innerCfg = innerBuildableCfg;
                m_innerCfg2 = innerBuildableCfg as IVsBuildableProjectCfg2;
                m_buildManager = buildManager;
                m_buildProjectConfiguration = buildProjectConfiguration;
            }

            public int AdviseBuildStatusCallback(IVsBuildStatusCallback pIVsBuildStatusCallback, out uint pdwCookie)
            {
                PrintCalled();
                pdwCookie = m_callbacks.Add(pIVsBuildStatusCallback);
                return VSConstants.S_OK;
            }

            public int GetBuildCfgProperty(int propid, out object pvar)
            {
                PrintCalled();
                switch ((__VSBLDCFGPROPID)propid)
                {
                    case __VSBLDCFGPROPID.VSBLDCFGPROPID_SupportsMTBuild:
                        // Indicate that we do not multi-proc builds
                        // This is needed so calls to get the next project to build on the UI
                        // thread will have a result in the case more than one project is built.
                        // NOTE: Build itself is still asynchronous.
                        pvar = false;
                        return VSConstants.S_OK;
                    default:
                        break;
                }

                return m_innerCfg2.GetBuildCfgProperty(propid, out pvar);
            }

            int IVsBuildableProjectCfg.get_ProjectCfg(out IVsProjectCfg ppIVsProjectCfg)
            {
                PrintCalled();
                ppIVsProjectCfg = null;
                return m_innerCfg?.get_ProjectCfg(out ppIVsProjectCfg) ?? VSConstants.S_OK;
            }

            public int QueryStartBuild(uint options, int[] supported, int[] ready)
            {
                if (supported != null && supported.Length > 0)
                {
                    supported[0] = 1;
                }

                if (ready != null && ready.Length > 0)
                {
                    ready[0] = 1;
                }

                return VSConstants.S_OK;
            }

            public int QueryStartClean(uint dwOptions, int[] pfSupported, int[] pfReady)
            {
                return VSConstants.S_OK;
            }

            public int QueryStartUpToDateCheck(uint dwOptions, int[] pfSupported, int[] pfReady)
            {
                // Does not support fast up to date check
                return VSConstants.S_OK;
            }

            public int QueryStatus(out int pfBuildDone)
            {
                pfBuildDone = 0;
                return VSConstants.S_OK;
            }

            public int StartBuild(IVsOutputWindowPane pIVsOutputWindowPane, uint dwOptions)
            {
                PrintCalled(pIVsOutputWindowPane);

                int continueParam = 1;
                foreach (IVsBuildStatusCallback callback in m_callbacks)
                {
                    callback.BuildBegin(ref continueParam);
                }

                string projectName = m_buildProjectConfiguration.m_projectName;

                var filter = GetPropertyValue(VsPackage.Constants.DominoBuildFilterProp);
                if (filter == null)
                {
                    var specFile = GetPropertyValue(VsPackage.Constants.DominoSpecFileProp);
                    if (specFile == null)
                    {
                        m_buildManager.WriteIncompatibleMessage(Path.GetFileName(projectName));
                    }
                    else
                    {
                        filter = SpecUtilities.GenerateSpecFilter(specFile);
                    }
                }

                StartBuildAsync(pIVsOutputWindowPane, projectName, filter);

                PrintAfterCalled(pIVsOutputWindowPane);
                return VSConstants.S_OK;
            }

            private string GetPropertyValue(string property)
            {
                string result = null;
                m_buildProjectConfiguration.m_buildPropertyStorage?.GetPropertyValue(
                    property,
                    pszConfigName: null,
                    storage: (uint)_PersistStorageType.PST_PROJECT_FILE,
                    pbstrPropValue: out result);
                return result;
            }

            [System.Diagnostics.CodeAnalysis.SuppressMessage("AsyncUsage", "AsyncFixer03:FireForgetAsyncVoid")]
            private async void StartBuildAsync(IVsOutputWindowPane pIVsOutputWindowPane, string projectName, string buildFilter)
            {
                var result = true;

                // pIVsOutputWindowPane.OutputStringThreadSafe($"Building '{projectName ?? string.Empty}' with outputDirectory '{outputDirectory ?? string.Empty}'\n");
                if (!string.IsNullOrEmpty(projectName) && !string.IsNullOrEmpty(buildFilter))
                {
                    result = await m_buildManager.BuildProjectAsync(
                        projectName: projectName,
                        filter: buildFilter);
                }

                int success = result ? 1 : 0;
                foreach (IVsBuildStatusCallback callback in m_callbacks)
                {
                    callback.BuildEnd(success);
                }
            }

            private static void PrintCalled(IVsOutputWindowPane outputWindow = null, [CallerMemberName] string memberName = null)
            {
                // m_traceBuildXLMessage($"BuildXLProjectConfiguration({m_buildProjectConfiguration.m_projectName ?? string.Empty}): {memberName}\n");
                // if (outputWindow != null)
                // {
                //    outputWindow.OutputString($"BuildXLProjectConfiguration: {memberName}\n");
                // }
            }

            private static void PrintAfterCalled(IVsOutputWindowPane outputWindow, [CallerMemberName] string memberName = null)
            {
                // m_traceBuildXLMessage($"BuildXLProjectConfiguration.After: {memberName}\n");
                // if (outputWindow != null)
                // {
                //  outputWindow.OutputString($"BuildXLProjectConfiguration.After: {memberName}\n");
                // }
            }

            public int StartBuildEx(uint dwBuildId, IVsOutputWindowPane pIVsOutputWindowPane, uint dwOptions)
            {
                return StartBuild(pIVsOutputWindowPane, dwOptions);
            }

            public int StartClean(IVsOutputWindowPane pIVsOutputWindowPane, uint dwOptions)
            {
                return VSConstants.S_OK;
            }

            public int StartUpToDateCheck(IVsOutputWindowPane pIVsOutputWindowPane, uint dwOptions)
            {
                return VSConstants.E_FAIL;
            }

            public int Stop(int fSync)
            {
                m_buildManager.CancelBuild();
                return VSConstants.S_OK;
            }

            public int UnadviseBuildStatusCallback(uint dwCookie)
            {
                m_callbacks.RemoveAt(dwCookie);
                return VSConstants.S_OK;
            }

            public int Wait(uint dwMilliseconds, int fTickWhenMessageQNotEmpty)
            {
                return VSConstants.E_NOTIMPL;
            }
        }
    }
}
