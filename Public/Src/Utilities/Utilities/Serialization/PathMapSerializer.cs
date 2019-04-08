// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Utilities.Collections;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Serializer for path maps.
    /// </summary>
    public class PathMapSerializer : IObserver<KeyValuePair<string, string>>
    {
        /// <summary>
        /// The version for format of <see cref="PathMapSerializer"/>.
        /// </summary>
        public const int FormatVersion = 1;

        private static readonly FileEnvelope s_fileEnvelope = new FileEnvelope(name: nameof(PathMapSerializer), version: FormatVersion);
        private readonly string m_filePath;

        private readonly PathTable m_pathTable;
        private readonly ConcurrentBigMap<AbsolutePath, AbsolutePath> m_pathMapping;

        /// <summary>
        /// Create an instance of <see cref="PathMapSerializer"/>.
        /// </summary>
        public PathMapSerializer(string filePath)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(filePath));

            m_filePath = filePath;
            m_pathTable = new PathTable();
            m_pathMapping = new ConcurrentBigMap<AbsolutePath, AbsolutePath>();
        }

        /// <inheritdoc />
        void IObserver<KeyValuePair<string, string>>.OnCompleted()
        {
            Contract.Assert(m_filePath != null);
            Save(m_filePath);
        }

        /// <inheritdoc />
        public void OnError(Exception error)
        {
            throw Contract.AssertFailure("This method should not be called");
        }

        /// <inheritdoc />
        public void OnNext(KeyValuePair<string, string> value)
        {
            var source = AbsolutePath.Create(m_pathTable, value.Key);
            var target = AbsolutePath.Create(m_pathTable, value.Value);
            m_pathMapping[source] = target;
        }

        /// <summary>
        /// Loads path map from a file.
        /// </summary>
        public static async Task<ConcurrentBigMap<AbsolutePath, AbsolutePath>> LoadAsync(string fileName, PathTable pathTable)
        {
            FileStream fileStream = ExceptionUtilities.HandleRecoverableIOException(
                () =>
                    new FileStream(
                        fileName,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read | FileShare.Delete,
                        bufferSize: 4096,
                        options: FileOptions.None),
                ex => { throw new BuildXLException(I($"Failed to open '{fileName}'"), ex); });

            using (fileStream)
            {
                return await LoadAsync(fileStream, fileName, pathTable);
            }
        }

        private static Task<ConcurrentBigMap<AbsolutePath, AbsolutePath>> LoadAsync(Stream stream, string fileName, PathTable pathTable)
        {
            return ExceptionUtilities.HandleRecoverableIOException(
                async () =>
                {
                    s_fileEnvelope.ReadHeader(stream);
                    using (BuildXLReader reader = new BuildXLReader(debug: false, stream: stream, leaveOpen: true))
                    {
                        var stringTable = await StringTable.DeserializeAsync(reader);
                        var loadedPathTable = await PathTable.DeserializeAsync(reader, Task.FromResult(stringTable));

                        var importedPathIndex = pathTable.Import(loadedPathTable);

                        var pathMapping = new ConcurrentBigMap<AbsolutePath, AbsolutePath>();

                        var count = reader.ReadInt32();
                        for (int i = 0; i < count; i++)
                        {
                            var loadedKey = reader.ReadAbsolutePath();
                            var loadedValue = reader.ReadAbsolutePath();

                            var key = importedPathIndex[loadedKey.Value.Index];
                            var value = importedPathIndex[loadedValue.Value.Index];

                            pathMapping[key] = value;
                        }

                        return pathMapping;
                    }
                },
                ex => { throw new BuildXLException(I($"Failed to read '{fileName}'"), ex); });
        }

        internal void Save(string fileName)
        {
            FileStream fileStream = ExceptionUtilities.HandleRecoverableIOException(
                () =>
                    new FileStream(
                        fileName,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.Delete,
                        bufferSize: 4096,
                        options: FileOptions.None),
                ex => { throw new BuildXLException(I($"Failed to open '{fileName}'"), ex); });

            using (fileStream)
            {
                Save(fileStream, fileName);
            }
        }

        internal void Save(Stream stream, string fileName)
        {
            ExceptionUtilities.HandleRecoverableIOException(
                () =>
                {
                    // We don't have anything in particular to correlate this file to,
                    // so we are simply creating a unique correlation id that is used as part
                    // of the header consistency check.
                    FileEnvelopeId correlationId = FileEnvelopeId.Create();
                    s_fileEnvelope.WriteHeader(stream, correlationId);

                    using (BuildXLWriter writer = new BuildXLWriter(debug: false, stream: stream, leaveOpen: true, logStats: false))
                    {
                        m_pathTable.StringTable.Serialize(writer);
                        m_pathTable.Serialize(writer);

                        writer.Write(m_pathMapping.Count);

                        foreach (var kvp in m_pathMapping)
                        {
                            writer.Write(kvp.Key);
                            writer.Write(kvp.Value);
                        }
                    }

                    s_fileEnvelope.FixUpHeader(stream, correlationId);
                    return (object)null;
                },
                ex => { throw new BuildXLException(I($"Failed to write to '{fileName}'"), ex); });
        }
    }
}
