// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
