// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Text;
using BuildXL.Native.IO;
using BuildXL.Utilities.Core;

namespace BuildXL.Processes
{
    /// <summary>
    /// Handler for stdout and stderr incremental output from a sandboxed process.
    /// Calls a stream observer if configured, and stores the first maxMemoryLength characters
    /// in a memory buffer (StringBuilder). If stream redirection is configured and the memory
    /// buffer overflows, writes the stream to a backing file.
    /// </summary>
    internal sealed class SandboxedProcessOutputBuilder : IDisposable
    {
        private readonly SandboxedProcessFile m_file;
        private readonly ISandboxedProcessFileStorage m_fileStorage;
        private readonly int m_maxMemoryLength;
        private readonly Action<string> m_observer;

        private string m_fileName;
        private long m_length;
        private readonly PooledObjectWrapper<StringBuilder> m_stringBuilderWrapper;
        private StringBuilder m_stringBuilder;
        private TextWriter m_textWriter;
        private BuildXLException m_exception;

        internal Encoding Encoding { get; }

        public SandboxedProcessOutputBuilder(
            Encoding encoding,
            int maxMemoryLength,
            ISandboxedProcessFileStorage fileStorage,
            SandboxedProcessFile file,
            Action<string> observer)
        {
            Contract.Requires(encoding != null);
            Contract.Requires(maxMemoryLength >= 0);

            HookOutputStream = (fileStorage != null || observer != null);

            m_stringBuilderWrapper = Pools.GetStringBuilder();
            m_stringBuilder = m_stringBuilderWrapper.Instance;

            Encoding = encoding;
            m_maxMemoryLength = maxMemoryLength;
            m_fileStorage = fileStorage;
            m_file = file;
            m_observer = observer;
        }

        private void ReleaseTextWriter()
        {
            var textWriter = m_textWriter;
            if (textWriter != null)
            {
                m_textWriter = null;
                HandleRecoverableIOException(textWriter.Dispose);
            }
        }

        private void ReleaseStringBuilder()
        {
            if (m_stringBuilder != null)
            {
                m_stringBuilder = null;
                m_stringBuilderWrapper.Dispose();
            }
        }

        [SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed", MessageId = "m_textWriter")]
        public void Dispose()
        {
            ReleaseTextWriter();
            ReleaseStringBuilder();
            IsFrozen = true;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public bool AppendLine(string data)
        {
            if (m_exception != null)
            {
                return true;
            }

            Contract.Assert(!IsFrozen);

            if (data == null)
            {
                ReleaseTextWriter();
                IsFrozen = true;
            }
            else
            {
                m_observer?.Invoke(data);

                m_length += data.Length + Environment.NewLine.Length;
                if (m_textWriter != null)
                {
                    HandleRecoverableIOException(() => m_textWriter.WriteLine(data));
                }
                else if (m_fileStorage == null && m_stringBuilder.Length >= m_maxMemoryLength)
                {
                    // The caller should have configured an observer, called above. If not and no backing file configured,
                    // we start silently dropping the output stream at the in-memory buffer length.
                }
                else
                {
                    m_stringBuilder.AppendLine(data);
                    Contract.Assert(m_stringBuilder.Length == m_length);
                    if (m_length > m_maxMemoryLength && m_fileStorage != null)
                    {
                        m_fileName = m_fileStorage.GetFileName(m_file);
                        HandleRecoverableIOException(
                            () =>
                            {
                                FileUtilities.CreateDirectory(Path.GetDirectoryName(m_fileName));

                                // Note that we use CreateReplacementFile since the target may be a read-only hardlink (e.g. in the build cache).
                                FileStream stream = FileUtilities.CreateReplacementFile(
                                    m_fileName,
                                    FileShare.Read | FileShare.Delete,
                                    openAsync: false);
                                m_textWriter = new StreamWriter(stream, Encoding);
                                m_textWriter.Write(m_stringBuilder.ToString());
                                ReleaseStringBuilder();
                            });
                    }
                }
            }

            return true;
        }

        private void HandleRecoverableIOException(Action action)
        {
            try
            {
                ExceptionUtilities.HandleRecoverableIOException(
                    action,
                    ex => { throw new BuildXLException("Writing file failed", ex); });
            }
            catch (BuildXLException ex)
            {
                m_exception = ex;
                ReleaseTextWriter();
                ReleaseStringBuilder();
                m_fileName = null;
                m_length = SandboxedProcessOutput.NoLength;
                IsFrozen = true;
            }
        }

        /// <summary>
        /// Whether this builder has been frozen
        /// </summary>
        public bool IsFrozen { get; private set; }

        /// <summary>
        /// Whether the process wrapper should hook this stream, i.e. whether an observer is configured.
        /// When false, the stream output should be allowed to stream to the parent console.
        /// </summary>
        public bool HookOutputStream { get; }

        /// <summary>
        /// Obtain finalized output
        /// </summary>
        public SandboxedProcessOutput Freeze()
        {
            ReleaseTextWriter();
            IsFrozen = true;
            return new SandboxedProcessOutput(
                m_length,
                m_stringBuilder?.ToString(),
                m_fileName,
                Encoding,
                m_fileStorage,
                m_file,
                m_exception);
        }
    }
}
