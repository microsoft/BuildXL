// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Text;
using BuildXL.Cache.ContentStore.Hashing;

namespace BuildXL.Ipc.ExternalApi.Commands
{
    /// <summary>
    /// Command corresponding to the <see cref="Client.GetContentLocationInBlobStorage(ContentHash)"/> API operation.
    /// </summary>
    public sealed class GetContentLocationInBlobStorage : Command<Uri>
    {
        /// <summary>
        /// Content hash for which the location in blob storage is requested.
        /// </summary>
        public ContentHash Hash { get; }

        /// <nodoc />
        public GetContentLocationInBlobStorage(ContentHash contentHash)
        {
            Hash = contentHash;
        }

        /// <inheritdoc />
        public override bool TryParseResult(string result, out Uri commandResult)
        {
            if (result == string.Empty)
            {
                commandResult = null;
            }

            return Uri.TryCreate(result, UriKind.Absolute, out commandResult);
        }

        /// <inheritdoc />
        public override string RenderResult(Uri contentLocation)
        {
            if (contentLocation == null)
            {
                return string.Empty;
            }

            return contentLocation.ToString();
        }

        internal override void InternalSerialize(BinaryWriter writer)
        {
            Hash.Serialize(writer);
        }

        internal static Command InternalDeserialize(BinaryReader reader)
        {
            var hash = new ContentHash(reader);
            return new GetContentLocationInBlobStorage(hash);
        }
    }
}