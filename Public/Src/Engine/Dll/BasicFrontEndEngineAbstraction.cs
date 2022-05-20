// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.FileSystem;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Engine
{
    /// <summary>
    /// Basic implementation of FrontEndEngineAbstraction.
    /// Adds explicitly settable mount table on top of <see cref="SimpleFrontEndEngineAbstraction"/>.
    /// </summary>
    public class BasicFrontEndEngineAbstraction : SimpleFrontEndEngineAbstraction
    {
        /// <nodoc />
        public BasicFrontEndEngineAbstraction(PathTable pathTable, IFileSystem fileSystem, IConfiguration configuration = null)
            : base(pathTable, fileSystem, configuration)
        {
        }

        /// <summary>
        /// Adds all the mounts specified in the given mounts table
        /// </summary>
        public void UpdateMountsTable(MountsTable mountsTable)
        {
            var allMounts = mountsTable.AllMountsSoFar.ToDictionary(mount => mount.Name.ToString(m_pathTable.StringTable), mount => mount);

            if (m_customMountsTable == null)
            {
                m_customMountsTable = allMounts;
            }
            else
            {
                foreach (var mount in allMounts)
                {
                    m_customMountsTable[mount.Key] = mount.Value;
                }
            }
        }

        /// <summary>
        /// Creates a default mount table with the regular system and configuration defined mounts and sets it.
        /// </summary>
        public bool TryPopulateWithDefaultMountsTable(LoggingContext loggingContext, BuildXLContext buildXLContext, IConfiguration configuration, IReadOnlyDictionary<string, string> properties)
        {
            var mountsTable = MountsTable.CreateAndRegister(loggingContext, buildXLContext, configuration, properties);
            
            if (!mountsTable.CompleteInitialization())
            {
                return false;
            }

            UpdateMountsTable(mountsTable);

            return true;
        }
    }
}
