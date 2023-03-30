// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO.Pipes;
using System.Text;
using BuildXL.Processes.Internal;

namespace BuildXL.Processes
{
    /// <summary>
    /// Factory for pipe reader.
    /// </summary>
    public static class PipeReaderFactory
    {
        private static Kind s_kind = Kind.Legacy;

        /// <summary>
        /// Kinds of pipe reader.
        /// </summary>
        public enum Kind
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
        internal static IAsyncPipeReader CreateManagedPipeReader(
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
        private static Kind GetKind()
        {
            return s_kind;
        }

        /// <summary>
        /// Sets kind of pipe reader to be created.
        /// </summary>
        /// <remarks>
        /// For NET6 or greater, the default is StreamReader-based async pipe reader. For other runtimes,
        /// the legacy one is chosen.
        /// </remarks>
        public static void SetKind(string kind)
        {
#if NET6_0_OR_GREATER
            if (!string.IsNullOrEmpty(kind))
            {
                s_kind = Enum.TryParse(kind, true, out Kind value) ? value : Kind.Legacy;
            }
            else
            {
                s_kind = Kind.Legacy;
            }
#endif
        }

        /// <summary>
        /// Checks if BuildXL should use the legacy async pipe reader.
        /// </summary>
        /// <returns></returns>
        public static bool ShouldUseLegacyPipeReader() => GetKind() == Kind.Legacy;
    }
}
