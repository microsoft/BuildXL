// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Ide.LanguageServer.Tracing;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Instrumentation.Common;
using JetBrains.Annotations;

namespace BuildXL.Ide.LanguageServer.Providers
{
    /// <summary>
    /// Groups common app-related objects useful for the language providers
    /// </summary>
    public sealed class ProviderContext
    {
        /// <nodoc/>
        [NotNull]
        public StreamJsonRpc.JsonRpc JsonRpc { get; }

        /// <nodoc/>
        [NotNull]
        public IncrementalWorkspaceProvider IncrementalWorkspaceProvider { get; }

        /// <nodoc/>
        [NotNull]
        public PathTable PathTable { get; }

        /// <nodoc/>
        public TestContext? TestContext { get; }

        /// <nodoc/>
        [NotNull]
        public Logger Logger { get; }

        /// <nodoc/>
        [NotNull]
        public LoggingContext LoggingContext { get; }

        /// <nodoc/>
        [NotNull]
        public GetAppState GetAppState { get; }

        /// <nodoc/>
        public ProviderContext(
            [NotNull] StreamJsonRpc.JsonRpc jsonRpc,
            [NotNull] IncrementalWorkspaceProvider incrementalWorkspaceProvider,
            [NotNull] PathTable pathTable,
            [NotNull] Logger logger,
            [NotNull] LoggingContext loggingContext,
            [NotNull] GetAppState getAppState,
            TestContext? testContext = null)
        {
            JsonRpc = jsonRpc;
            IncrementalWorkspaceProvider = incrementalWorkspaceProvider;
            PathTable = pathTable;
            TestContext = testContext;
            Logger = logger;
            LoggingContext = loggingContext;
            GetAppState = getAppState;
        }
    }
}
