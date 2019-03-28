// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

export const mounts = Context.getCurrentHost().os === "macOS" ? [
    {
        name: a`xcode`,
        path: p`/Applications`,
        trackSourceFileChanges: true,
        isWritable: false,
        isReadable: true,
        isScrubbable: false,
    },
    {
        name: a`etc`,
        path: p`/etc`,
        trackSourceFileChanges: true,
        isWritable: false,
        isReadable: true,
        isScrubbable: false,
    },
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
    {
        name: a`user`,
        path: p`/Users/${Environment.getStringValue("USER")}`,
        trackSourceFileChanges: true,
        isWritable: false,
        isReadable: true,
        isScrubbable: false,
    },
    {
        name: a`privateetc`,
        path: p`/private/etc`,
        trackSourceFileChanges: true,
        isWritable: false,
        isReadable: true,
        isScrubbable: false,
    },
    {
        name: a`tmp`,
        path: p`/tmp`,
        trackSourceFileChanges: false,
        isWritable: false,
        isReadable: true,
        isScrubbable: false,
    }
] : [];