// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities.ParallelAlgorithms;

namespace Test.BuildXL.Utilities.ParallelAlgorithmsTests
{
    internal static class ParallelAlgorithmsHelper
    {
        public static Task WaitUntilOrFailAsync(
            Func<bool> predicate,
            TimeSpan? pollInterval = null,
            TimeSpan? timeout = null,
            [CallerArgumentExpression("predicate")]
            string predicateMessage = "")
        {
            return ParallelAlgorithms.WaitUntilOrFailAsync(
                predicate,
                pollInterval ?? TimeSpan.FromMilliseconds(100),
                timeout ?? TimeSpan.FromSeconds(5),
                predicateMessage);
        }
    }
}