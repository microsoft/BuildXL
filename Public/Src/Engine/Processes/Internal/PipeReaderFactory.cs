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
            /// Default or original async pipe reader.
            /// </summary>
            Default,

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
        /// Creates a pipe reader.
        /// </summary>
        public static IAsyncPipeReader CreateNonDefaultPipeReader(
            NamedPipeServerStream pipeStream,
            StreamDataReceived callback,
            Encoding encoding,
            int bufferSize)
        {
            if (GetKind() == Kind.Pipeline)
            {
#if NET_COREAPP_60
                return new PipelineAsyncPipeReader(pipeStream, callback, encoding);
#endif
            }

            // Fall back to use StreamReader based one.
            return new StreamAsyncPipeReader(pipeStream, callback, encoding, bufferSize);
        }

        /// <summary>
        /// Gets kind of pipe reader to be created.
        /// </summary>
        public static Kind GetKind()
        {
            if (string.IsNullOrEmpty(EngineEnvironmentSettings.SandboxAsyncPipeReaderKind.Value))
            {
                return Kind.Default;
            }

            return Enum.TryParse(EngineEnvironmentSettings.SandboxAsyncPipeReaderKind.Value, true, out Kind value) ? value : Kind.Default;
        }
    }
}
