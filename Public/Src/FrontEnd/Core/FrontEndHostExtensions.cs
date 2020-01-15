// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Workspaces.Core;

namespace BuildXL.FrontEnd.Core
{
    /// <nodoc />
    public static class FrontEndHostExtensions
    {
        /// <summary>
        /// Returns workspace by downcasting <see cref="BuildXL.FrontEnd.Sdk.Workspaces.IWorkspace"/> to <see cref="Workspace"/>
        /// </summary>
        public static Workspace GetWorkspace(this FrontEndHost host)
        {
            return (Workspace)host.Workspace;
        }
    }
}
