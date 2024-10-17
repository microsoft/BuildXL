// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.Utilities.Core.Qualifier;

namespace BuildXL.Utilities.Core
{
    /// <summary>
    /// Core container of context objects
    /// </summary>
    public abstract class BuildXLContext : PipExecutionContext
    {
        private readonly TokenTextTable m_tokenTextTable;

        private readonly IntPtr m_consoleWindowsHandle;

        /// <summary>
        /// protected constructor
        /// </summary>
        protected BuildXLContext(BuildXLContext context)
            : this(
            context.CancellationToken,
            context.StringTable,
            context.PathTable,
            context.SymbolTable,
            context.QualifierTable,
            context.TokenTextTable,
            context.ConsoleWindowsHandle)
        {
            Contract.RequiresNotNull(context);
        }

        /// <summary>
        /// Must create a derived class for your component
        /// </summary>
        protected BuildXLContext(
            CancellationToken cancellationToken,
            StringTable stringTable,
            PathTable pathTable,
            SymbolTable symbolTable,
            QualifierTable qualifierTable,
            TokenTextTable tokenTextTable,
            IntPtr consoleWindowsHandle)
            : base(
                cancellationToken,
                stringTable,
                pathTable,
                symbolTable,
                qualifierTable)
        {
            Contract.RequiresNotNull(tokenTextTable);

            m_tokenTextTable = tokenTextTable;
            m_consoleWindowsHandle = consoleWindowsHandle;
        }

        /// <summary>
        /// Creates a new context for testing purposes only. Real components should create a derived class
        /// </summary>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "BuildXLContext takes ownership for disposal.")]
        public static BuildXLContext CreateInstanceForTesting()
        {
            var stringTable = new StringTable();
            var pathTable = new PathTable(stringTable);
            var symbolTable = new SymbolTable(stringTable);
            var qualifierTable = new QualifierTable(stringTable);
            var tokenTextTable = new TokenTextTable();

            return new BuildXLTestContext(stringTable, pathTable, symbolTable, qualifierTable, tokenTextTable, CancellationToken.None);
        }

        /// <summary>
        /// Creates a new context for testing purposes only using existing context and cancellation token
        /// Real components should create a derived class
        /// </summary>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "BuildXLContext takes ownership for disposal.")]
        public static BuildXLContext CreateInstanceForTestingWithCancellationToken(BuildXLContext context, CancellationToken cancellationToken)
        {
            return new BuildXLTestContext(context.StringTable, context.PathTable, context.SymbolTable, context.QualifierTable, context.TokenTextTable, cancellationToken) { TestHooks = context.TestHooks };
        }

        /// <summary>
        /// TokenTextTable for this invocation;
        /// </summary>
        public TokenTextTable TokenTextTable
        {
            get
            {
                return m_tokenTextTable;
            }
        }

        /// <summary>
        /// A handle to the owner console window where BuildXL is running.
        /// </summary>
        /// <remarks>
        /// If BuildXL is running with server mode enabled, the handle represents the owner of the client window.
        /// </remarks>
        public IntPtr ConsoleWindowsHandle => m_consoleWindowsHandle;

        /// <summary>
        /// Invalidates the context to prevent future use
        /// </summary>
        public virtual void Invalidate()
        {
            StringTable.Invalidate();
            PathTable.Invalidate();
            SymbolTable.Invalidate();
            TokenTextTable.Invalidate();
        }

        /// <summary>
        /// Private class for testing purposes
        /// </summary>
        private sealed class BuildXLTestContext : BuildXLContext
        {
            /// <summary>
            /// Constructs a new instance
            /// </summary>
            public BuildXLTestContext(
                StringTable stringTable,
                PathTable pathTable,
                SymbolTable symbolTable,
                QualifierTable qualifierTable,
                TokenTextTable tokenTextTable,
                CancellationToken cancellationToken)
                : base(
                    cancellationToken,
                    stringTable,
                    pathTable,
                    symbolTable,
                    qualifierTable,
                    tokenTextTable,
                    consoleWindowsHandle: IntPtr.Zero)
            {
            }
        }
    }
}
