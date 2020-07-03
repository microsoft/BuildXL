// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Native.Streams
{
    internal sealed class IOCompletionNotificationArgs
    {
        public IIOCompletionTarget Target;
        public FileAsyncIOResult Result;
    }
}
