// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Ipc.ExternalApi;
using BuildXL.Storage;
using BuildXL.Utilities.Core;

namespace Tool.BlobDaemon
{
    internal record FileToUpload(
        Client ApiClient,
        string FilePath,
        string FileId,
        FileContentInfo FileContentInfo,
        UploadLocation UploadLocation,
        string AuthVar
    )
    {
        public FileArtifact FileArtifact { get; } = BuildXL.Ipc.ExternalApi.FileId.Parse(FileId);
    }
}
