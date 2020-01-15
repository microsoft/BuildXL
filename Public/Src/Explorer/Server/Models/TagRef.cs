// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities;

namespace BuildXL.Explorer.Server.Models
{
    public class TagRef
    {
        public TagRef(PipExecutionContext context, StringId name)
        {
            Name = name.ToString(context.StringTable);
        }
        public TagRef(string name)
        {
            Name = name;
        }

        public string Name { get; set; }
    }
}
