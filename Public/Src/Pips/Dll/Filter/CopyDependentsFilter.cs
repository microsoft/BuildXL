// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;

namespace BuildXL.Pips.Filter
{
    /// <summary>
    /// Dependents filter which only includes copy pips dependents
    /// </summary>
    public class CopyDependentsFilter : ClosureFunctionFilter<CopyDependentsFilter>
    {
        /// <summary>
        /// Class constructor
        /// </summary>
        public CopyDependentsFilter(PipFilter inner)
            : base(inner)
        {
        }

        /// <inheritdoc/>
        protected override IEnumerable<PipId> GetNeighborPips(IPipFilterContext context, PipId pip)
        {
            return context.GetDependents(pip).Where(p => context.GetPipType(p) == BuildXL.Pips.Operations.PipType.CopyFile);
        }

        /// <inheritdoc/>
        protected override CopyDependentsFilter CreateFilter(PipFilter inner)
        {
            return new CopyDependentsFilter(inner);
        }
    }
}
