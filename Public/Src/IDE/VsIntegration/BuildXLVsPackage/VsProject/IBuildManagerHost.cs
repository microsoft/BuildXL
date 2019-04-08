// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.VsPackage.VsProject
{
    /// <summary>
    /// Represents callbacks required from the host for building using
    /// BuildXL build manager
    /// </summary>
    public interface IBuildManagerHost
    {
        /// <summary>
        /// Writes a build message
        /// </summary>
        void WriteBuildMessage(string message);

        /// <summary>
        /// Queries whether there are more projects which the IDE will build in the
        /// current build operation.
        /// </summary>
        bool HasMoreProjects();
    }
}
