// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.Utilities.Qualifier;

namespace BuildXL.Utilities
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
                Contract.Ensures(Contract.Result<PathTable>() != null);
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
                Contract.Ensures(Contract.Result<SymbolTable>() != null);
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
                Contract.Ensures(Contract.Result<QualifierTable>() != null);
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
                Contract.Ensures(Contract.Result<StringTable>() != null);
                return m_stringTable;
            }
        }

        /// <summary>
        /// Protected constructor
        /// </summary>
        protected PipExecutionContext(PipExecutionContext context)
            : this(
                context.CancellationToken,
                context.StringTable,
                context.PathTable,
                context.SymbolTable,
                context.QualifierTable)
        {
            Contract.Requires(context != null);
        }

        /// <summary>
        /// Protected constructor
        /// </summary>
        protected PipExecutionContext(
            CancellationToken cancellationToken,
            StringTable stringTable,
            PathTable pathTable,
            SymbolTable symbolTable,
            QualifierTable qualifierTable)
        {
            Contract.Requires(stringTable != null);
            Contract.Requires(pathTable != null);
            Contract.Requires(symbolTable != null);
            Contract.Requires(qualifierTable != null);
            Contract.Requires(stringTable == pathTable.StringTable);
            Contract.Requires(stringTable == symbolTable.StringTable);
            Contract.Requires(stringTable == qualifierTable.StringTable);

            CancellationToken = cancellationToken;
            m_stringTable = stringTable;
            m_pathTable = pathTable;
            m_symbolTable = symbolTable;
            m_qualifierTable = qualifierTable;
        }
    }
}
