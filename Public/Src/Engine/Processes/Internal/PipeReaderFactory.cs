// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO.Pipes;
using System.Text;
using BuildXL.Utilities.Configuration;

namespace BuildXL.Processes.Internal
{
    /// <summary>
    /// Factory for pipe reader.
    /// </summary>
    internal static class PipeReaderFactory
    {
        /// <summary>
        /// Kinds of pipe reader.
        /// </summary>
        internal enum Kind
        {
            /// <summary>
            /// Lagacy async pipe reader.
            /// </summary>
            /// <remarks>
            /// Legacy async pipe reader is based on IO completion. It mysteriously
            /// causes an issue in .NET6 where the pipe reading is cancelled out of nowhere in
            /// the middle of process execution. Attempt to retry pipe reading only works on short
            /// running processes.
            /// </remarks>
            Legacy,

            /// <summary>
            /// StreamReader-based async pipe reader.
            /// </summary>
            Stream,

            /// <summary>
            /// Pipeline-based async pipe reader.
            /// </summary>
            Pipeline
        }

        /// <summary>
        /// Creates a managed pipe reader.
        /// </summary>
        /// <remarks>
        /// A managed pipe reader only use API provided by .NET for its implementation.
        /// Currently there are two kinds of managed pipe reader, StreamAsyncPipeReader that
        /// is based on .NET stream reader and PipelineAsyncPipeReader that is based on .NET System.IO.Pipelines.
        /// </remarks>
        public static IAsyncPipeReader CreateManagedPipeReader(
            NamedPipeServerStream pipeStream,
            StreamDataReceived callback,
            Encoding encoding,
            int bufferSize,
            Kind? overrideKind = default)
        {
            Kind kind = overrideKind ?? GetKind();

            if (kind == Kind.Pipeline)
            {
#if NET6_0_OR_GREATER
                return new PipelineAsyncPipeReader(pipeStream, callback, encoding);
#endif
            }

            // Fall back to use StreamReader based one.
            return new StreamAsyncPipeReader(pipeStream, callback, encoding, bufferSize);
        }

        /// <summary>
        /// Gets kind of pipe reader to be created.
        /// </summary>
        /// <remarks>
        /// For NET6 or greater, the default is StreamReader-based async pipe reader. For other runtimes,
        /// the legacy one is chosen.
        /// </remarks>
        private static Kind GetKind()
        {
#if NET6_0_OR_GREATER
            if (string.IsNullOrEmpty(EngineEnvironmentSettings.SandboxAsyncPipeReaderKind.Value))
            {
                return Kind.Stream;
            }

            return Enum.TryParse(EngineEnvironmentSettings.SandboxAsyncPipeReaderKind.Value, true, out Kind value) ? value : Kind.Stream;
#else
            return Kind.Legacy;
#endif
        }

        /// <summary>
        /// Checks if BuildXL should use the legacy async pipe reader.
        /// </summary>
        /// <returns></returns>
        public static bool ShouldUseLegacyPipeReader() => GetKind() == Kind.Legacy;
    }
}
