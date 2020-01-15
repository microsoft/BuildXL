// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities;

namespace BuildXL.Explorer.Server.Models
{
    public class FileRef : PathRef
    {
        public FileRef(PipExecutionContext context, FileArtifact file)
            : base(context, file.Path)
        {

        }
        public string Kind => "file";
    }
}
