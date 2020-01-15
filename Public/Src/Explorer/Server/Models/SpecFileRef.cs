// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities;

namespace BuildXL.Explorer.Server.Models
{
    public class SpecFileRef : PathRef
    {
        public SpecFileRef(PipExecutionContext context, AbsolutePath path)
            : base(context, path)
        {

        }
    }
}
