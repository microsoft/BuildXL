// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Explorer.Server.Models
{
    public class PageResult<T>
    {
        public int Page { get; set; }

        public int Count { get; set; }

        public IEnumerable<T> Items { get; set; }
    }

}
