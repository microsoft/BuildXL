// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.ContentStore.Vfs
{
    public class Placeholder
    {
        public static T Todo<T>(string message = null, T value = default)
        {
            return value;
        }
    }
}
