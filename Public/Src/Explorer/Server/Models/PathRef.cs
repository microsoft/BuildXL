// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;

namespace BuildXL.Explorer.Server.Models
{
    public class PathRef
    {
        public PathRef(PipExecutionContext context, AbsolutePath path)
        {
            Id = path.Value.Value;
            FileName = path.GetName(context.PathTable).ToString(context.StringTable);
            FilePath = path.ToString(context.PathTable);
        }

        public int Id { get; set; }
        public string FileName { get; set; }
        public string FilePath { get; set; }
    }
}
