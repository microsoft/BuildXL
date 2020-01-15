// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
