// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Qualifier;

namespace BuildXL.FrontEnd.Script.RuntimeModel
{
    /// <summary>
    /// Context preparing runtime model.
    /// </summary>
    public sealed class RuntimeModelContext
    {
        private readonly FrontEndContext m_frontEndContext;

        /// <summary>
        /// Front-end host.
        /// </summary>
        public FrontEndHost FrontEndHost { get; private set; }

        /// <summary>
        /// String table.
        /// </summary>
        public StringTable StringTable => m_frontEndContext.StringTable;

        /// <summary>
        /// Symbol table.
        /// </summary>
        public SymbolTable SymbolTable => m_frontEndContext.SymbolTable;

        /// <summary>
        /// Path table.
        /// </summary>
        public PathTable PathTable => m_frontEndContext.PathTable;

        /// <summary>
        /// Qualifier table.
        /// </summary>
        public QualifierTable QualifierTable => m_frontEndContext.QualifierTable;

        /// <summary>
        /// Logging context.
        /// </summary>
        public LoggingContext LoggingContext => m_frontEndContext.LoggingContext;

        /// <summary>
        /// Cancellation token.
        /// </summary>
        public CancellationToken CancellationToken => m_frontEndContext.CancellationToken;

        /// <summary>
        /// Logger instance that should be for reporting diagnostics.
        /// </summary>
        public Logger Logger { get; }

        /// <summary>
        /// Root path.
        /// </summary>
        public AbsolutePath RootPath { get; }

        /// <summary>
        /// Origin that triggers the parsing.
        /// </summary>
        public LocationData Origin { get; }

        /// <summary>
        /// Package.
        /// </summary>
        public Package Package { get; }

        /// <nodoc />
        public RuntimeModelContext(
            FrontEndHost frontEndHost,
            FrontEndContext frontEndContext,
            Logger logger,
            Package package,
            LocationData origin = default(LocationData))
        {
            Contract.Requires(frontEndHost != null);
            Contract.Requires(frontEndContext != null);
            Contract.Requires(package != null);

            FrontEndHost = frontEndHost;
            m_frontEndContext = frontEndContext;
            Package = package;
            RootPath = package.Path.GetParent(frontEndContext.PathTable);
            Origin = origin;
            Logger = logger;
        }
    }
}
