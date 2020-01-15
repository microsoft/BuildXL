// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities;

namespace BuildXL.Explorer.Server.Models
{
    public class DirectoryRef : PathRef
    {
        public DirectoryRef(PipExecutionContext context, AbsolutePath path)
            : base(context, path)
        {

        }
        public string Kind => "directory";
    }
}
