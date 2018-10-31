// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Native.Streams
{
    internal sealed class IOCompletionNotificationArgs
    {
        public IIOCompletionTarget Target;
        public FileAsyncIOResult Result;
    }
}
