// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

config({
    modules: [
        d`sdk`,
        d`src`,
        d`test`
    ].mapMany(dir => [...globR(dir, "module.config.dsc"), ...globR(dir, "package.config.dsc")]),

    mounts: Context.getCurrentHost().os === "macOS" ? [
        {
            name: a`usrbin`,
            path: p`/usr/bin`,
            trackSourceFileChanges: true,
            isWritable: false,
            isReadable: true,
            isScrubbable: false,
        },
        {
            name: a`usrlib`,
            path: p`/usr/lib`,
            trackSourceFileChanges: true,
            isWritable: false,
            isReadable: true,
            isScrubbable: false,
        },
        {
            name: a`usrinclude`,
            path: p`/usr/include`,
            trackSourceFileChanges: true,
            isWritable: false,
            isReadable: true,
            isScrubbable: false,
        },
        {
            name: a`library`,
            path: p`/Library`,
            trackSourceFileChanges: true,
            isWritable: false,
            isReadable: true,
            isScrubbable: false,
        },
    ] : []
});
