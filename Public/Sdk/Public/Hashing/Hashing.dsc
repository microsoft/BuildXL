// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Hashing {
    /** Returns the SHA 256 of content */
    @@public
    export function sha256(content: string): string
    {
        return _PreludeAmbientHack_Hashing.sha256(content);
    }
}
