// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.FileSystem;

namespace BuildXL.Engine
{
    /// <summary>
    /// Basic implementation of FrontEndEngineAbstraction.
    /// Adds explicitly settable mount table on top of <see cref="SimpleFrontEndEngineAbstraction"/>.
    /// </summary>
    public class BasicFrontEndEngineAbstraction : SimpleFrontEndEngineAbstraction
    {
        private MountsTable m_mountsTable;

        /// <nodoc />
        public BasicFrontEndEngineAbstraction(PathTable pathTable, IFileSystem fileSystem, IConfiguration configuration = null)
            : base(pathTable, fileSystem, configuration)
        {
        }

        /// <inheritdoc />
        public override IEnumerable<string> GetMountNames(string frontEnd, ModuleId moduleId)
        {
            if (m_customMountsTable != null)
            {
                return m_customMountsTable.Keys;
            }

            return m_mountsTable?.GetMountNames(moduleId) ?? Enumerable.Empty<string>();
        }

        /// <inheritdoc />
        public override TryGetMountResult TryGetMount(string name, string frontEnd, ModuleId moduleId, out IMount mount)
        {
            mount = null;

            if (string.IsNullOrEmpty(name))
            {
                return TryGetMountResult.NameNullOrEmpty;
            }

            if (m_customMountsTable?.TryGetValue(name, out mount) == true)
            {
                return TryGetMountResult.Success;
            }

            return m_mountsTable == null ? TryGetMountResult.NameNotFound : m_mountsTable.TryGetMount(name, moduleId, out mount);
        }

        /// <nodoc />
        public void SetMountsTable(MountsTable mountsTable)
        {
            m_mountsTable = mountsTable;
        }
    }
}
