// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AdoBuildRunner
{
    /// <summary>
    /// Defines the role of the machine in an Ado environment.
    /// </summary>
    public enum MachineRole
    {
        /// <nodoc />
        Orchestrator,

        /// <nodoc />
        Worker
    }
}
