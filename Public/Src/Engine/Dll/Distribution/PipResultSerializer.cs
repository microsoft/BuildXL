// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using BuildXL.Utilities;
using System.Collections.Concurrent;
using BuildXL.Pips;
using BuildXL.Scheduler.Distribution;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using BuildXL.Scheduler;
using BuildXL.Pips.Operations;
using Google.Protobuf;

namespace BuildXL.Engine.Distribution
{
    internal interface IPipResultSerializer
    {
        void SerializeExecutionResult(ExtendedPipCompletionData completionData);
    }

    internal sealed class PipResultSerializer : IPipResultSerializer
    {
        #region Writer Pool

        private readonly ObjectPool<BuildXLWriter> m_writerPool = new(CreateWriter, CleanupWriter);

        private static void CleanupWriter(BuildXLWriter writer)
        {
            writer.BaseStream.SetLength(0);
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope", Justification = "Disposal is not needed for memory stream")]
        private static BuildXLWriter CreateWriter()
        {
            return new BuildXLWriter(
                debug: false,
                stream: new MemoryStream(),
                leaveOpen: false,
                logStats: false);
        }

        #endregion Writer Pool

        private readonly ExecutionResultSerializer m_serializer;

        public PipResultSerializer(ExecutionResultSerializer serializer)
        {
            m_serializer = serializer;
        }

        public void SerializeExecutionResult(ExtendedPipCompletionData completionData)
        {
            using (var pooledWriter = m_writerPool.GetInstance())
            {
                var writer = pooledWriter.Instance;
                m_serializer.Serialize(writer, completionData.ExecutionResult, completionData.PreservePathSetCasing);
                
                // Write from the beginning of the stream
                writer.BaseStream.Position = 0;
                completionData.SerializedData.ResultBlob = ByteString.FromStream(writer.BaseStream);
            }
        }
    }
}