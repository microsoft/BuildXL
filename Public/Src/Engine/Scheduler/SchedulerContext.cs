// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.Utilities;
using BuildXL.Utilities.Qualifier;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Context used by the Scheduler for scheduling and executing pips
    /// </summary>
    public sealed class SchedulerContext : PipExecutionContext
    {
        /// <summary>
        /// Context used during schedule execution
        /// </summary>
        public SchedulerContext(
            CancellationToken cancellationToken,
            StringTable stringTable,
            PathTable pathTable,
            SymbolTable symbolTable,
            QualifierTable qualifierTable)
            : base(
                cancellationToken,
                stringTable,
                pathTable,
                symbolTable,
                qualifierTable)
        {
            Contract.Requires(stringTable != null);
            Contract.Requires(pathTable != null);
            Contract.Requires(symbolTable != null);
            Contract.Requires(qualifierTable != null);
        }

        /// <summary>
        /// Context used during schedule execution
        /// </summary>
        public SchedulerContext(BuildXLContext context)
            : this(
                context.CancellationToken,
                context.StringTable,
                context.PathTable,
                context.SymbolTable,
                context.QualifierTable)
        {
            Contract.Requires(context != null);
        }
    }
}
