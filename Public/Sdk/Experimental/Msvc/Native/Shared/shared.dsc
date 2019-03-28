// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

@@public
export interface PlatformDependentQualifier extends Qualifier {
    configuration: "debug" | "release";
    platform: "x86" | "x64";
}

export declare const qualifier : PlatformDependentQualifier;

import * as Cl              from "Sdk.Native.Tools.Cl";
import * as CPreprocessor   from "Sdk.Native.Tools.CPreprocessor";
import * as EtwManifest     from "Sdk.Native.Tools.EtwManifest";
import * as Lib             from "Sdk.Native.Tools.Lib";
import * as Link            from "Sdk.Native.Tools.Link";
import * as Mc              from "Sdk.Native.Tools.Mc";
import * as Midl            from "Sdk.Native.Tools.Midl";
import * as Rc              from "Sdk.Native.Tools.Rc";
import * as Wpp             from "Sdk.Native.Tools.Wpp";

export {
    Cl,
    CPreprocessor,
    EtwManifest,
    Lib,
    Link,
    Mc,
    Midl,
    Rc,
    Wpp
};
