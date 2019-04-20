// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Processes
{
    /// <summary>
    /// The output of a sandboxes process, stored either in memory or on disk
    /// </summary>
    public sealed class SandboxedProcessOutput
    {
        /// <summary>
        /// An undefined file length
        /// </summary>
        public const long NoLength = -1;

        private readonly Encoding m_encoding;
        private readonly SandboxedProcessFile m_file;
        private readonly ISandboxedProcessFileStorage m_fileStorage;
        private readonly long m_length;
        private readonly string m_value;
        private string m_fileName;
        private Task m_saveTask;
        private readonly BuildXLException m_exception;

        /// <summary>
        /// Creates an instances of this class.
        /// </summary>
        public SandboxedProcessOutput(
            long length,
            string value,
            string fileName,
            Encoding encoding,
            ISandboxedProcessFileStorage fileStorage,
            SandboxedProcessFile file,
            BuildXLException exception)
        {
            Contract.Requires((fileName == null && length >= 0) || (fileName != null && length >= NoLength) || exception != null);
            Contract.Requires(exception != null ^ (value != null ^ fileName != null));
            Contract.Requires(value == null || length == value.Length);
            Contract.Requires(exception != null || encoding != null);
            Contract.Requires(exception != null || fileName != null || fileStorage != null);
            Contract.Requires(encoding != null);

            m_length = length;
            m_value = value;
            m_fileName = fileName;
            m_encoding = encoding;
            m_fileStorage = fileStorage;
            m_file = file;
            m_saveTask = m_fileName != null ? Unit.VoidTask : null;
            m_exception = exception;
        }

        /// <summary>
        /// Serializes this instance to a given <paramref name="writer"/>.
        /// </summary>
        public void Serialize(BuildXLWriter writer)
        {
            writer.Write(m_length);
            writer.WriteNullableString(m_value);
            writer.WriteNullableString(m_fileName);
            writer.Write(m_encoding.CodePage);
            writer.Write(m_fileStorage, (w, v) => SandboxedProcessStandardFiles.From(v).Serialize(w));
            writer.WriteCompact((uint)m_file);
            writer.Write(m_exception, (w, v) =>
            {
                w.WriteNullableString(v.Message);
                w.WriteCompact((uint)v.RootCause);
            });
        }

        /// <summary>
        /// Deserializes an instance of <see cref="SandboxedProcessOutput"/>.
        /// </summary>
        public static SandboxedProcessOutput Deserialize(BuildXLReader reader)
        {
            long length = reader.ReadInt64();
            string value = reader.ReadNullableString();
            string fileName = reader.ReadNullableString();

            int codePage = reader.ReadInt32();

#if DISABLE_FEATURE_EXTENDED_ENCODING
            Encoding encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
#else
            Encoding encoding = Encoding.GetEncoding(codePage);
#endif
            SandboxedProcessStandardFiles standardFiles = reader.ReadNullable(r => SandboxedProcessStandardFiles.Deserialize(r));
            ISandboxedProcessFileStorage fileStorage = null;
            if (standardFiles != null)
            {
                fileStorage = new StandardFileStorage(standardFiles);
            }
            SandboxedProcessFile file = (SandboxedProcessFile)reader.ReadUInt32Compact();
            BuildXLException exception = reader.ReadNullable(r => new BuildXLException(r.ReadNullableString(), (ExceptionRootCause)r.ReadUInt32Compact()));

            return new SandboxedProcessOutput(
                length,
                value,
                fileName,
                encoding,
                fileStorage,
                file,
                exception);
        }

        /// <summary>
        /// The encoding used when saving the file
        /// </summary>
        public Encoding Encoding
        {
            get
            {
                Contract.Requires(!HasException);
                Contract.Ensures(Contract.Result<Encoding>() != null);
                return m_encoding;
            }
        }

        /// <summary>
        /// Re-creates an instance from a saved file.
        /// </summary>
        public static SandboxedProcessOutput FromFile(string fileName, string encodingName, SandboxedProcessFile file)
        {
            Contract.Requires(fileName != null);
            Contract.Requires(encodingName != null);
            Contract.Ensures(Contract.Result<SandboxedProcessOutput>() != null);

            BuildXLException exception = null;
            Encoding encoding;

#if DISABLE_FEATURE_EXTENDED_ENCODING
            // Console encoding is forced to UTF-8 in CoreFx see: https://github.com/dotnet/corefx/issues/10054
            // Trying to parse anything else or in the Windows case 'Codepage - 437', fails. We default to UTF-8 which
            // is the standard console encoding on any Unix based system anyway.
            encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
#else
            try
            {
                try
                {
                    encoding = Encoding.GetEncoding(encodingName);
                }
                catch (ArgumentException ex)
                {
                    throw new BuildXLException("Unsupported encoding name", ex);
                }
            }
            catch (BuildXLException ex)
            {
                fileName = null;
                encoding = null;
                exception = ex;
            }
#endif

            return new SandboxedProcessOutput(NoLength, null, fileName, encoding, null, file, exception);
        }

        /// <summary>
        /// The kind of output file
        /// </summary>
        public SandboxedProcessFile File => m_file;

        /// <summary>
        /// The length of the output in characters
        /// </summary>
        public long Length
        {
            get
            {
                Contract.Requires(HasLength);
                return m_length;
            }
        }

        /// <summary>
        /// Reads the entire value; don't call this if the length is excessive, as it might OOM.
        /// </summary>
        /// <exception cref="BuildXLException">Thrown if a recoverable error occurs while opening the stream.</exception>
        public async Task<string> ReadValueAsync()
        {
            if (m_exception != null)
            {
                ExceptionDispatchInfo.Capture(m_exception).Throw();
            }

            if (m_value != null)
            {
                return m_value;
            }

            return await ExceptionUtilities.HandleRecoverableIOException(
                async () =>
                {
                    using (TextReader reader = CreateFileReader())
                    {
                        string value = await reader.ReadToEndAsync();
                        return value;
                    }
                },
                e => throw new BuildXLException("Failed to read a value from a stream", e));
        }

        /// <summary>
        /// Checks whether the file has been saved to disk
        /// </summary>
        [Pure]
        public bool IsSaved => Volatile.Read(ref m_fileName) != null;

        /// <summary>
        /// Checks whether this instance is in an exceptional state.
        /// </summary>
        public bool HasException => m_exception != null;

        /// <summary>
        /// Checks whether the length of this instance is known.
        /// </summary>
        public bool HasLength => m_length > NoLength;

        /// <summary>
        /// Saves the file to disk
        /// </summary>
        /// <exception cref="BuildXLException">Thrown if a recoverable error occurs while opening the stream.</exception>
        public Task SaveAsync()
        {
            if (m_exception != null)
            {
                ExceptionDispatchInfo.Capture(m_exception).Throw();
            }

            if (m_saveTask == null)
            {
                lock (this)
                {
                    if (m_saveTask == null)
                    {
                        m_saveTask = InternalSaveAsync();
                    }
                }
            }

            return m_saveTask;
        }

        private async Task InternalSaveAsync()
        {
            string fileName = m_fileStorage.GetFileName(m_file);
            FileUtilities.CreateDirectory(Path.GetDirectoryName(fileName));
            await FileUtilities.WriteAllTextAsync(fileName, m_value, m_encoding);

            string existingFileName = Interlocked.CompareExchange(ref m_fileName, fileName, comparand: null);
            Contract.Assume(existingFileName == null, "Sandboxed process output should only be saved once (via InternalSaveAsync)");

            m_saveTask = Unit.VoidTask;
        }

        /// <summary>
        /// Gets the name of a file that stores the output
        /// </summary>
        public string FileName
        {
            get
            {
                Contract.Requires(IsSaved);
                Contract.Ensures(Contract.Result<string>() != null);
                return m_fileName;
            }
        }

        /// <summary>
        /// Creates a reader for the output.
        /// </summary>
        /// <exception cref="BuildXLException">Thrown if a recoverable error occurs while opening the stream.</exception>
        public TextReader CreateReader()
        {
            Contract.Ensures(Contract.Result<TextReader>() != null);

            if (m_exception != null)
            {
                throw m_exception;
            }

            return m_value != null ? new StringReader(m_value) : CreateFileReader();
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "The StreamReader will dispose the stream.")]
        private TextReader CreateFileReader()
        {
            // This FileStream is not asynchronous due to an intermittant crash we see on some machines when using
            // an asynchronous stream here
            FileStream stream = FileUtilities.CreateFileStream(
                m_fileName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete);
            return new StreamReader(stream);
        }
    }
}
