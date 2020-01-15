// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
