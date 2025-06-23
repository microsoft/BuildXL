// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

module({
    name: "BuildXL.FrontEndUnitTests",
    mounts: addIfLazy(!Context.isWindowsOS(), ()=> [
        { 
            name: a`frontendut-temp`, 
            path: p`/tmp`,
            isReadable: true,
            isWritable: true,
            isScrubbable: true
         }
    ])
});
