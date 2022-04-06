// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Utilities;

namespace BuildXL.Processes.Internal
{
    /// <summary>
    /// Asynchronous pipe reader based on <see cref="StreamReader"/>.
    /// </summary>
    /// <remarks>
    /// This implementation is recommended by .NET team, but micro benchmarking shows that it is 2x slower
    /// than the original pipe reader as well as the pipeline-based async pipe reader.
    /// </remarks>
    internal sealed class StreamAsyncPipeReader : IAsyncPipeReader
    {
        private readonly StreamDataReceived m_userCallBack;
        private readonly NamedPipeServerStream m_pipeStream;
        private readonly StreamReader m_reader;
        private Task m_completionTask = Task.CompletedTask;

        /// <summary>
        /// Constructor.
        /// </summary>
        public StreamAsyncPipeReader(
            NamedPipeServerStream pipeStream,
            StreamDataReceived callback,
            Encoding encoding,
            int bufferSize)
        {
            m_pipeStream = pipeStream;
            m_userCallBack = callback;
            m_reader = new StreamReader(pipeStream, encoding, false, bufferSize);
        }

        /// <inheritdoc/>
        public void BeginReadLine() => m_completionTask = ReadAsync();

        private async Task ReadAsync()
        {
            try
            {
                while (true)
                {
                    string line = await m_reader.ReadLineAsync();
                    m_userCallBack?.Invoke(line);

                    if (line == null)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // BuildXL never cancels pipe reading. So, cancelation may only happen
                // when the build is forcefully canceled (e.g., due to Ctrl-C or reaching build timeout).
                // Do nothing.
            }
            catch (Exception ex)
            {
                throw new BuildXLException("Exception occured when reading from pipe", ex);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_reader.Dispose();
            m_pipeStream.Dispose();
        }

        /// <inheritdoc/>
        public Task CompletionAsync(bool waitForEof) => m_completionTask;
    }
}