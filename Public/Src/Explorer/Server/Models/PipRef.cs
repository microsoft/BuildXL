// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Explorer.Server.Models
{
    public class PipRef
    {
        public int Id { get; set; }
        public string Kind { get; set; }
        public string SemiStableHash { get; set; }
        public string ShortDescription { get; set; }
    }

}
