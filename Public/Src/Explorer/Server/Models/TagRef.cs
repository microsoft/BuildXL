// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
