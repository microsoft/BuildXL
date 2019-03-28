// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Flavor;
using Microsoft.VisualStudio.Shell.Interop;

namespace BuildXL.VsPackage.VsProject
{
    /// <nodoc />
    public sealed class ProjectFlavor : FlavoredProjectBase, IVsProjectFlavorCfgProvider, IVsDependencyProvider
    {
        private IVsProjectFlavorCfgProvider m_innerVsProjectFlavorCfgProvider;
        private BuildProjectConfiguration m_projectConfiguration;
        private readonly BuildManager m_buildManager;
        private readonly Action<string> m_traceDominoMessage;

        private readonly EmptyDependencies m_emptyDependencies = new EmptyDependencies();
        private string m_projectName;

        /// <nodoc />
        public ProjectFlavor(IServiceProvider serviceProvider, BuildManager buildManager, Action<string> traceDominoMessage)
        {
            this.serviceProvider = serviceProvider;
            m_buildManager = buildManager;
            m_traceDominoMessage = traceDominoMessage;
        }

        /// <summary>
        /// This should first QI for (and keep a reference to) each interface we plan to call on the inner project
        /// and then call the base implementation to do the rest. Because the base implementation
        /// already keep a reference to the interfaces it override, we don't need to QI for those.
        /// </summary>
        protected override void SetInnerProject(IntPtr innerIUnknown)
        {
            object inner = Marshal.GetObjectForIUnknown(innerIUnknown);

            // Now let the base implementation set the inner object
            base.SetInnerProject(innerIUnknown);

            m_innerVsProjectFlavorCfgProvider = inner as IVsProjectFlavorCfgProvider;
        }

        #region IVsProjectFlavorCfgProvider Implementation

        int IVsProjectFlavorCfgProvider.CreateProjectFlavorCfg(IVsCfg pBaseProjectCfg, out IVsProjectFlavorCfg ppFlavorCfg)
        {
            ppFlavorCfg = null;
            if (m_projectConfiguration == null)
            {
                object projectExtObj = null;
                var hr = _innerVsHierarchy?.GetProperty((uint)VSConstants.VSITEMID.Root, (int)__VSHPROPID.VSHPROPID_ExtObject, out projectExtObj);
                var buildPropertyStorage = _innerVsHierarchy as IVsBuildPropertyStorage;

                EnvDTE.Project dteProject = projectExtObj as EnvDTE.Project;

                if (dteProject != null)
                {
                    m_projectName = dteProject.FullName;
                }

                if (m_innerVsProjectFlavorCfgProvider != null)
                {
                    m_innerVsProjectFlavorCfgProvider.CreateProjectFlavorCfg(pBaseProjectCfg, out ppFlavorCfg);
                }

                m_projectConfiguration = new BuildProjectConfiguration(pBaseProjectCfg, ppFlavorCfg, m_buildManager, m_traceDominoMessage, buildPropertyStorage, m_projectName);
            }

            ppFlavorCfg = m_projectConfiguration;

            return VSConstants.S_OK;
        }

        #endregion IVsProjectFlavorCfgProvider Implementation

        #region IVsDependencyProvider Implementation

        // Implement IVsDependencyProvider to remove all dependencies between projects. BuildXL builds require that the full
        // set of built projects is known at the start of the build. This contrasts with VS dependency-first build ordering
        // and the fact that VS does not expose the set of projects requested to build. To work around this, we remove ALL dependencies so
        // VS always calls all projects to build without consideration for dependency order.
        int IVsDependencyProvider.EnumDependencies(out IVsEnumDependencies ppIVsEnumDependencies)
        {
            // Use empty set of dependencies for BuildXL projects so all projects get called when building
            // a specific set (i.e., startup projects for F5 or for running unit tests). Otherwise, VS will
            // build in dependency first order and there is no way to get the full set of projects that will be
            // built.
            ppIVsEnumDependencies = m_emptyDependencies;
            return VSConstants.S_OK;
        }

        int IVsDependencyProvider.OpenDependency(string szDependencyCanonicalName, out IVsDependency ppIVsDependency)
        {
            ppIVsDependency = null;
            return VSConstants.S_OK;
        }

        private class EmptyDependencies : IVsEnumDependencies
        {
            public int Clone(out IVsEnumDependencies ppIVsEnumDependencies)
            {
                ppIVsEnumDependencies = this;
                return VSConstants.S_OK;
            }

            public int Next(uint cElements, IVsDependency[] rgpIVsDependency, out uint pcElementsFetched)
            {
                pcElementsFetched = 0;
                return VSConstants.S_FALSE;
            }

            public int Reset()
            {
                return VSConstants.S_OK;
            }

            public int Skip(uint cElements)
            {
                return VSConstants.S_FALSE;
            }
        }

        #endregion IVsDependencyProvider Implementation
    }
}
