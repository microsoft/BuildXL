// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
import * as Deployment from "Sdk.Deployment";
@@public
export const deployment : Deployment.Definition = {
    contents: [
        f`jsonSdk.dsc`,
        f`module.config.dsc`
    ]
};