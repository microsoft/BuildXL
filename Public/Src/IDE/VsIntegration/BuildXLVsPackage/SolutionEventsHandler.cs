// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;

namespace BuildXL.VsPackage
{
    /// <summary>
    /// Handles solution events
    /// </summary>
    public sealed class SolutionEventsHandler : IVsSolutionEvents
    {
        /// <summary>
        /// instance of the main package
        /// </summary>
        private readonly BuildXLVsPackage m_package;

        /// <summary>
        /// Main constructor of the solution events handler
        /// </summary>
        /// <param name="package">Main instance of the package that instantiated this object</param>
        public SolutionEventsHandler(BuildXLVsPackage package)
        {
            Contract.Requires(package != null);
            m_package = package;
        }

        /// <summary>
        /// Gets invoked when a project is opened
        /// </summary>
        /// <param name="pHierarchy">The hierarchy instance</param>
        /// <param name="fAdded">The ID of added</param>
        /// <returns>Returns the status whether added or not</returns>
        public int OnAfterOpenProject(IVsHierarchy pHierarchy, int fAdded)
        {
            // Helps to re-register if the project is unloaded and reloaded
            object project;
            ErrorHandler.ThrowOnFailure(pHierarchy.GetProperty(
                VSConstants.VSITEMID_ROOT,
                (int)__VSHPROPID.VSHPROPID_ExtObject,
                out project));

            m_package.RegisterEventsForProject(project as Project, pHierarchy);
            return VSConstants.S_OK;
        }

        /// <inheritdoc/>
        public int OnBeforeCloseProject(IVsHierarchy pHierarchy, int fRemoved)
        {
            m_package.DeRegisterEventsForProject(pHierarchy);
            return VSConstants.S_OK;
        }

        /// <inheritdoc/>
        public int OnAfterCloseSolution(object pUnkReserved)
        {
            return VSConstants.E_NOTIMPL;
        }

        /// <inheritdoc/>
        public int OnAfterLoadProject(IVsHierarchy pStubHierarchy, IVsHierarchy pRealHierarchy)
        {
            return VSConstants.E_NOTIMPL;
        }

        /// <inheritdoc/>
        public int OnAfterOpenSolution(object pUnkReserved, int fNewSolution)
        {
            return VSConstants.E_NOTIMPL;
        }

        /// <inheritdoc/>
        public int OnBeforeCloseSolution(object pUnkReserved)
        {
            return VSConstants.E_NOTIMPL;
        }

        /// <inheritdoc/>
        public int OnBeforeUnloadProject(IVsHierarchy pRealHierarchy, IVsHierarchy pStubHierarchy)
        {
            return VSConstants.E_NOTIMPL;
        }

        /// <inheritdoc/>
        public int OnQueryCloseProject(IVsHierarchy pHierarchy, int fRemoving, ref int pfCancel)
        {
            return VSConstants.E_NOTIMPL;
        }

        /// <inheritdoc/>
        public int OnQueryCloseSolution(object pUnkReserved, ref int pfCancel)
        {
            return VSConstants.E_NOTIMPL;
        }

        /// <inheritdoc/>
        public int OnQueryUnloadProject(IVsHierarchy pRealHierarchy, ref int pfCancel)
        {
            return VSConstants.E_NOTIMPL;
        }
    }
}
