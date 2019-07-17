// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
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
