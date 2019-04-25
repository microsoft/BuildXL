// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Analyzer.Tracing;
using BuildXL.FrontEnd.Workspaces;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.Analyzer
{
    /// <summary>
    /// Context passed to per-node analysis rules.
    /// </summary>
    public sealed class DiagnosticsContext
    {
        /// <nodoc />
        public DiagnosticsContext(ISourceFile sourceFile, Logger logger, LoggingContext loggingContext, PathTable pathTable, Workspace workspace)
        {
            Logger = logger;
            LoggingContext = loggingContext;
            PathTable = pathTable;

            Workspace = workspace;
            SemanticModel = workspace?.GetSemanticModel();
            SourceFile = sourceFile;
        }

        /// <nodoc />
        public Logger Logger { get; }

        /// <nodoc />
        public LoggingContext LoggingContext { get; }

        /// <nodoc />
        public PathTable PathTable { get; set; }

        /// <nodoc />
        public Workspace Workspace { get; }

        /// <nodoc />
        public ISemanticModel SemanticModel { get; }

        /// <nodoc />
        public ISourceFile SourceFile { get; }
    }
}
