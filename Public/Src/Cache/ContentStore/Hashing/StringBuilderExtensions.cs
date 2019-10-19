// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text;

namespace BuildXL.Cache.ContentStore.Hashing
{
    internal static class StringBuilderExtensions
    {
        public static unsafe void AppendCharStar(this StringBuilder builder, int length, char* buffer)
        {
#if NET_FRAMEWORK_451
            // The API that takes char* is not available in .NET Framework 4.5
            builder.Append(new string(buffer, 0, length));
#else
            builder.Append(buffer, length);
#endif
        }
    }
}
