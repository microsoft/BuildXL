// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.FrontEnd.Sdk;

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
