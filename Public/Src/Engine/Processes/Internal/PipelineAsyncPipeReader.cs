// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if NET6_0_OR_GREATER

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Utilities.Core;

namespace BuildXL.Processes.Internal
{
    /// <summary>
    /// Pipeline based pipe reader.
    /// </summary>
    internal sealed class PipelineAsyncPipeReader : IAsyncPipeReader
    {
        private readonly Encoding m_encoding;
        private readonly StreamDataReceived m_userCallBack;
        private readonly NamedPipeServerStream m_pipeStream;
        private readonly PipeReader m_reader;
        private readonly Queue<string> m_messageQueue = new ();
        private readonly byte[] m_newLineBytes;
        private Task m_completionTask = Task.CompletedTask;

        /// <summary>
        /// Constructor.
        /// </summary>
        public PipelineAsyncPipeReader(
            NamedPipeServerStream pipeStream,
            StreamDataReceived callback,
            Encoding encoding)
        {
            m_pipeStream = pipeStream;
            m_userCallBack = callback;
            m_encoding = encoding;
            m_reader = PipeReader.Create(pipeStream);

            m_newLineBytes = m_encoding.GetBytes(Environment.NewLine);
        }

        /// <inheritdoc/>
        public void BeginReadLine() => m_completionTask = ReadAsync();

        private async Task ReadAsync()
        {
            try
            {
                while (true)
                {
                    ReadResult readResult = await m_reader.ReadAsync();
                    ReadOnlySequence<byte> buffer = readResult.Buffer;

                    try
                    {
                        if (readResult.IsCanceled)
                        {
                            break;
                        }

                        if (TryParseLines(ref buffer))
                        {
                            FlushMessages();
                        }

                        if (readResult.IsCompleted)
                        {
                            if (!buffer.IsEmpty)
                            {
                                throw new BuildXLException("Incomplete pipe read: " + GetRemainingMessages(ref buffer));
                            }

                            break;
                        }
                    }
                    finally
                    {
                        m_reader.AdvanceTo(buffer.Start, buffer.End);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new BuildXLException("Exception occured when reading from pipe", ex);
            }
            finally
            {
                await m_reader.CompleteAsync();
                FlushMessages();
            }
        }

        private bool TryParseLines(ref ReadOnlySequence<byte> buffer)
        {
            while (true)
            {
                var reader = new SequenceReader<byte>(buffer);

                if (!reader.TryReadTo(out ReadOnlySequence<byte> line, m_newLineBytes))
                {
                    break;
                }

                buffer = buffer.Slice(reader.Position);
                string message = m_encoding.GetString(line);
                m_messageQueue.Enqueue(message);
            }

            return m_messageQueue.Count > 0;
        }

        private string GetRemainingMessages(ref ReadOnlySequence<byte> buffer) => buffer.IsEmpty ? null : m_encoding.GetString(buffer);

        private void FlushMessages()
        {
            while (m_messageQueue.Count > 0)
            {
                string message = m_messageQueue.Dequeue();
                bool? result = m_userCallBack?.Invoke(message);
                if (result == false)
                {
                    break;
                }
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_pipeStream.Dispose();
        }

        /// <inheritdoc/>
        public Task CompletionAsync(bool waitForEof) => m_completionTask;
    }
}

#endif