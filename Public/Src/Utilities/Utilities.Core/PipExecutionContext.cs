// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.Utilities.Core.Qualifier;

namespace BuildXL.Utilities.Core
{
    /// <summary>
    /// Context needed for executing pips
    /// </summary>
    public abstract class PipExecutionContext
    {
        private readonly StringTable m_stringTable;
        private readonly PathTable m_pathTable;
        private readonly SymbolTable m_symbolTable;
        private readonly QualifierTable m_qualifierTable;
        private MemoryConservation m_memoryConservation;

        /// <summary>
        /// A token to cancel the execution
        /// </summary>
        public CancellationToken CancellationToken { get; }

        /// <summary>
        /// PathTable for this invocation;
        /// </summary>
        public PathTable PathTable
        {
            get
            {
                return m_pathTable;
            }
        }

        /// <summary>
        /// SymbolTable for this invocation;
        /// </summary>
        public SymbolTable SymbolTable
        {
            get
            {
                return m_symbolTable;
            }
        }

        /// <summary>
        /// QualifierTable for this invocation;
        /// </summary>
        public QualifierTable QualifierTable
        {
            get
            {
                return m_qualifierTable;
            }
        }

        /// <summary>
        /// StringTable for this invocation;
        /// </summary>
        public StringTable StringTable
        {
            get
            {
                return m_stringTable;
            }
        }

        /// <summary>
        /// Hooks used for unit and integration tests
        /// </summary>
        public TestHooks TestHooks { get; set; }

        /// <summary>
        /// Coordinates memory conservation for this invocation when enabled.
        /// </summary>
        public MemoryConservation MemoryConservation => Volatile.Read(ref m_memoryConservation);

        /// <summary>
        /// Protected constructor
        /// </summary>
        protected PipExecutionContext(PipExecutionContext context)
            : this(
                context.CancellationToken,
                context.StringTable,
                context.PathTable,
                context.SymbolTable,
                context.QualifierTable,
                context.MemoryConservation)
        {
            Contract.RequiresNotNull(context);
        }

        /// <summary>
        /// Protected constructor
        /// </summary>
        protected PipExecutionContext(
            CancellationToken cancellationToken,
            StringTable stringTable,
            PathTable pathTable,
            SymbolTable symbolTable,
            QualifierTable qualifierTable,
            MemoryConservation memoryConservation = null)
        {
            Contract.RequiresNotNull(stringTable);
            Contract.RequiresNotNull(pathTable);
            Contract.RequiresNotNull(symbolTable);
            Contract.RequiresNotNull(qualifierTable);
            Contract.Requires(stringTable == pathTable.StringTable);
            Contract.Requires(stringTable == symbolTable.StringTable);
            Contract.Requires(stringTable == qualifierTable.StringTable);

            CancellationToken = cancellationToken;
            m_stringTable = stringTable;
            m_pathTable = pathTable;
            m_symbolTable = symbolTable;
            m_qualifierTable = qualifierTable;
            m_memoryConservation = memoryConservation;
        }

        /// <summary>
        /// Enables memory conservation for this context.
        /// </summary>
        public MemoryConservation EnableMemoryConservation()
        {
            var memoryConservation = MemoryConservation;
            if (memoryConservation != null)
            {
                return memoryConservation;
            }

            var newMemoryConservation = new MemoryConservation();
            Pools.RegisterMemoryConservationTargets(newMemoryConservation);
            memoryConservation = Interlocked.CompareExchange(ref m_memoryConservation, newMemoryConservation, null);
            if (memoryConservation == null)
            {
                return newMemoryConservation;
            }

            return memoryConservation;
        }
    }
}
