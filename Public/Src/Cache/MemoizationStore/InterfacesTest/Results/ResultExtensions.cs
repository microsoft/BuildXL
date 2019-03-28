// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;

namespace BuildXL.Cache.MemoizationStore.InterfacesTest.Results
{
    public static class ResultExtensions
    {
        public static async Task<AddOrGetContentHashListResult> ShouldBeSuccess(this Task<AddOrGetContentHashListResult> result)
        {
            var r = await result;
            r.ShouldBeSuccess();
            return r;
        }
    }
}
