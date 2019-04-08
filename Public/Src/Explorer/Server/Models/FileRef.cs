// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
