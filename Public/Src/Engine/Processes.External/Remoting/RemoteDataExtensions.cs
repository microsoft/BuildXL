// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using Google.Protobuf;

namespace BuildXL.Processes.Remoting
{
    /// <summary>
    /// Extension class from <see cref="RemoteData"/>.
    /// </summary>
    public static class RemoteDataExtensions
    {
        /// <summary>
        /// Serializes an instance of <see cref="RemoteData"/> to a stream.
        /// </summary>
        public static void Serialize(this RemoteData remoteData, Stream stream)
        {
            remoteData.WriteTo(stream);
        }

        /// <summary>
        /// Deserializes an instance of <see cref="RemoteData"/> from a stream.
        /// </summary>
        public static RemoteData Deserialize(Stream stream) => RemoteData.Parser.ParseFrom(stream);
    }
}
