// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

module({
    name: "BuildXL.Core.UnitTests",
    mounts: addIfLazy(!Context.isWindowsOS(), ()=> [
        { 
            name: a`coreut-temp`, 
            path: p`/tmp`,
            isReadable: true,
            isWritable: true,
            isScrubbable: true
         }
    ])
});
