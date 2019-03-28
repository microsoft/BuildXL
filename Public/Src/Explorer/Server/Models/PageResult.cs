// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
