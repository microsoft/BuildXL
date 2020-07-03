// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.MsBuild
{
    /// <summary>
    /// Failure when scheduling projects due to a cycle in its dependencies
    /// </summary>
    public class CycleInProjectsFailure<TProject> : Failure
    {
        /// <nodoc/>
        public CycleInProjectsFailure(IEnumerable<TProject> cycle)
        {
            Contract.RequiresNotNull(cycle);
            Cycle = cycle;
        }

        /// <nodoc/>
        public IEnumerable<TProject> Cycle { get; }

        /// <inheritdoc/>
        public override BuildXLException CreateException()
        {
            return new BuildXLException(Describe());
        }

        /// <inheritdoc/>
        public override string Describe()
        {
            return I($"Cycle detected in project dependencies: {string.Join(" -> ", Cycle.Select(project => project.ToString()))}");
        }

        /// <inheritdoc/>
        public override BuildXLException Throw()
        {
            throw CreateException();
        }
    }
}
