// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace VSCode.DebugAdapter
{
    public abstract partial class Command<T>
    {
        public T Result { get; set; }
    }
}
