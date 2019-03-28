// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

config({
    resolvers: [
        {
            kind: "DScript",
            modules: [
                f`package.config.dsc`,
                f`../../../../../Sdk/Public/Prelude/Package.config.dsc`,
                f`../../../../../Sdk/Public/Transformers/package.config.dsc`,
            ]
        }
    ]
}); 
