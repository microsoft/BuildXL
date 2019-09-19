// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Ide.LanguageServer.Tracing;
using BuildXL.Utilities;
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
        [JetBrains.Annotations.NotNull]
        public StreamJsonRpc.JsonRpc JsonRpc { get; }

        /// <nodoc/>
        [JetBrains.Annotations.NotNull]
        public IncrementalWorkspaceProvider IncrementalWorkspaceProvider { get; }

        /// <nodoc/>
        [JetBrains.Annotations.NotNull]
        public PathTable PathTable { get; }

        /// <nodoc/>
        public TestContext? TestContext { get; }

        /// <nodoc/>
        [JetBrains.Annotations.NotNull]
        public Logger Logger { get; }

        /// <nodoc/>
        [JetBrains.Annotations.NotNull]
        public LoggingContext LoggingContext { get; }

        /// <nodoc/>
        [JetBrains.Annotations.NotNull]
        public GetAppState GetAppState { get; }

        /// <nodoc/>
        public ProviderContext(
            [JetBrains.Annotations.NotNull] StreamJsonRpc.JsonRpc jsonRpc,
            [JetBrains.Annotations.NotNull] IncrementalWorkspaceProvider incrementalWorkspaceProvider,
            [JetBrains.Annotations.NotNull] PathTable pathTable,
            [JetBrains.Annotations.NotNull] Logger logger,
            [JetBrains.Annotations.NotNull] LoggingContext loggingContext,
            [JetBrains.Annotations.NotNull] GetAppState getAppState,
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
