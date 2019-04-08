// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;

namespace TypeScript.Net
{
    /// <summary>
    /// Helper class that helps to track migration process.
    /// Should be removed once all typescript compiler would be migrated to C#.
    /// </summary>
    internal static class PlaceHolder
    {
        /// <summary>
        /// This is different than throwing a NotImplementedException
        /// in that it means that the code in question has been 'visited'
        /// but the implementation is not known. Distinguishes from auto-generated
        /// code with throw NotImplementedException
        /// </summary>
        public static Exception NotImplemented()
        {
            throw new NotImplementedException();
        }

        public static T NotImplemented<T>()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Marker that shows that functionality is not required in .NET implementation.
        /// </summary>
        public static void Skip()
        { }

        /// <nodoc />
        [SuppressMessage("Microsoft.Performance", "CA1801")]
        public static Exception SkipThrow(string message = null)
        {
            return new NotImplementedException();
        }
    }
}
