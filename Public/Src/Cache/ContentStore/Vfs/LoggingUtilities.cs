// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.CompilerServices;
using BuildXL.Cache.ContentStore.Logging;

namespace BuildXL.Cache.ContentStore.Vfs
{
    internal static class LoggingUtilities
    {
        public static T PerformOperation<T>(this Logger log, string args, Func<T> action, [CallerMemberName] string caller = null)
        {
            log.Debug($"{caller}({args})");
            var result = action();
            log.Debug($"{caller}({args}) => [{result}]");
            return result;
        }
    }
}
