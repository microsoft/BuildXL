// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Workspaces.Core
{
    /// <summary>
    /// Singleton class that assigns unique ModuleIds. Thread safe.
    /// </summary>
    /// <remarks>
    /// Considering there is integration works ahead to use the same module ids across our interpreter, ModuleIds start at an arbitrary high number (10000)
    /// to facilitate debugging.
    /// </remarks>
    public static class ModuleIdProvider
    {
        private const int Seed = 10000;
        private static int s_nextIdToAssign = Seed;

        /// <summary>
        /// Returns a unique ModuleId
        /// </summary>
        public static ModuleId GetNextId()
        {
            return new ModuleId(Interlocked.Increment(ref s_nextIdToAssign));
        }
    }
}
