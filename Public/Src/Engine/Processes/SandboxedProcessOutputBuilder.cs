// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Text;
using BuildXL.Native.IO;
using BuildXL.Utilities;

namespace BuildXL.Processes
{
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
            Contract.Requires(fileStorage != null);

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
            Contract.Ensures(IsFrozen);
            ReleaseTextWriter();
            ReleaseStringBuilder();
            IsFrozen = true;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public bool AppendLine(string data)
        {
            Contract.Ensures(data != null || IsFrozen);

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
                else
                {
                    m_stringBuilder.AppendLine(data);
                    Contract.Assert(m_stringBuilder.Length == m_length);
                    if (m_length > m_maxMemoryLength)
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
        /// Obtain finalized output
        /// </summary>
        public SandboxedProcessOutput Freeze()
        {
            Contract.Ensures(Contract.Result<SandboxedProcessOutput>() != null);
            Contract.Ensures(IsFrozen);

            ReleaseTextWriter();
            IsFrozen = true;
            return new SandboxedProcessOutput(
                m_length,
                m_stringBuilder == null ? null : m_stringBuilder.ToString(),
                m_fileName,
                Encoding,
                m_fileStorage,
                m_file,
                m_exception);
        }
    }
}
