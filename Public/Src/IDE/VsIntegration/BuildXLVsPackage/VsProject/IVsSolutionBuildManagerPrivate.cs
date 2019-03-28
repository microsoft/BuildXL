// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell.Interop;

namespace Microsoft.Internal.VisualStudio.Shell.Interop
{
    /// <summary>
    /// This interface is copied from Microsoft.Internal.VisualStudio.Shell.Interop.10.0.DesignTime.dll. We can
    /// cast to it since the solution build manager is a COM object and this is a COM interface. It is used for determining
    /// if the are more projects that will be built.
    /// </summary>
    [Guid("71F69689-99E1-46C1-9A70-D8673679F810")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IVsSolutionBuildManagerPrivate
    {
        /// <nodoc />
        int GetNextBuildItemForUIThread(out IVsHierarchy ppIVsHierarchy);
    }
}
