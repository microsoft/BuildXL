// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Native.IO;
using BuildXL.Native.IO.Windows;
using BuildXL.Utilities;

namespace BuildXL.Storage.ChangeJournalService.Protocol
{
    /// <summary>
    /// Additional data type marshalling for the change journal service wire protocol.
    /// </summary>
    public static class ChangeJournalServiceProtocolWireExtensions
    {
        /// <summary>
        /// Reads the data for a successful USN journal metadata query.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1011")]
        public static QueryUsnJournalData ReadUsnJournalData(this ChangeJournalServiceProtocolReader reader)
        {
            Contract.Requires(reader != null);

            var data = new QueryUsnJournalData
                       {
                           UsnJournalId = reader.ReadUInt64(),
                           FirstUsn = new Usn(reader.ReadUInt64()),
                           NextUsn = new Usn(reader.ReadUInt64()),
                           LowestValidUsn = new Usn(reader.ReadUInt64()),
                           MaxUsn = new Usn(reader.ReadUInt64()),
                           MaximumSize = reader.ReadUInt64(),
                           AllocationDelta = reader.ReadUInt64()
                       };

            return data;
        }

        /// <summary>
        /// Writes the data for a successful USN journal metadata query.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1011")]
        public static void WriteUsnJournalData(this ChangeJournalServiceProtocolWriter writer, QueryUsnJournalData data)
        {
            Contract.Requires(writer != null);
            Contract.Requires(data != null);

            writer.Write(data.UsnJournalId);
            writer.Write(data.FirstUsn.Value);
            writer.Write(data.NextUsn.Value);
            writer.Write(data.LowestValidUsn.Value);
            writer.Write(data.MaxUsn.Value);
            writer.Write(data.MaximumSize);
            writer.Write(data.AllocationDelta);
        }

        /// <summary>
        /// Reads the data for a USN journal metadata query (though it may have returned an error)>
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "QueryUsnJournalStatus")]
        public static QueryUsnJournalResult ReadQueryUsnJournalResult(this ChangeJournalServiceProtocolReader reader)
        {
            Contract.Requires(reader != null);

            var queryStatusValue = reader.ReadInt32();
            QueryUsnJournalStatus queryStatus;
            if (!EnumTraits<QueryUsnJournalStatus>.TryConvert(queryStatusValue, out queryStatus))
            {
                throw new BuildXLException("Invalid QueryUsnJournalStatus");
            }

            if (queryStatus == QueryUsnJournalStatus.Success)
            {
                QueryUsnJournalData data = reader.ReadUsnJournalData();
                return new QueryUsnJournalResult(queryStatus, data);
            }

            return new QueryUsnJournalResult(queryStatus, data: null);
        }

        /// <summary>
        /// Writes the data for a USN journal metadata query (though it may have returned an error)>
        /// </summary>
        public static void WriteQueryUsnJournalResult(this ChangeJournalServiceProtocolWriter writer, QueryUsnJournalResult result)
        {
            Contract.Requires(writer != null);
            Contract.Requires(result != null);

            writer.Write((int)result.Status);
            if (result.Succeeded)
            {
                writer.WriteUsnJournalData(result.Data);
            }
        }

        /// <summary>
        /// Reads a volume GUID path. Throws in the event of an invalid path.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1011")]
        public static VolumeGuidPath ReadVolumeGuidPath(this ChangeJournalServiceProtocolReader reader)
        {
            Contract.Requires(reader != null);
            Contract.Ensures(Contract.Result<VolumeGuidPath>().IsValid);

            string maybePath = reader.ReadString();
            VolumeGuidPath path;
            if (!VolumeGuidPath.TryCreate(maybePath, out path))
            {
                throw new BuildXLException("Expected a volume GUID path");
            }

            return path;
        }

        /// <summary>
        /// Writes a volume guid path.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1011")]
        public static void WriteVolumeGuidPath(this ChangeJournalServiceProtocolWriter writer, VolumeGuidPath volumeGuidPath)
        {
            Contract.Requires(writer != null);
            Contract.Requires(volumeGuidPath.IsValid);

            writer.Write(volumeGuidPath.Path);
        }

        /// <summary>
        /// Reads a <see cref="ReadUsnJournalStatus"/>.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1011")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "ReadUsnJournalStatus")]
        public static ReadUsnJournalStatus ReadUsnJournalReadStatus(this ChangeJournalServiceProtocolReader reader)
        {
            Contract.Requires(reader != null);

            var readStatusValue = reader.ReadByte();
            ReadUsnJournalStatus readStatus;
            if (!EnumTraits<ReadUsnJournalStatus>.TryConvert(readStatusValue, out readStatus))
            {
                throw new BuildXLException("Invalid ReadUsnJournalStatus");
            }

            return readStatus;
        }

        /// <summary>
        /// Writes a <see cref="ReadUsnJournalStatus"/>.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1011")]
        public static void WriteUsnJournalReadStatus(this ChangeJournalServiceProtocolWriter writer, ReadUsnJournalStatus status)
        {
            Contract.Requires(writer != null);

            writer.Write((byte)status);
        }
    }
}
