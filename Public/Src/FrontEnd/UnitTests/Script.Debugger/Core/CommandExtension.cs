// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace VSCode.DebugAdapter
{
    public abstract partial class Command<T>
    {
        public T Result { get; set; }
    }
}
