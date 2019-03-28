// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

config({
    modules: [
        d`sdk`,
        d`src`,
        d`test`
    ].mapMany(dir => [...globR(dir, "module.config.dsc"), ...globR(dir, "package.config.dsc")])
});
