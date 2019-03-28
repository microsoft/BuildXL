// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

export declare const qualifier : {
    configuration: "debug" | "release",
    platform: "x86" | "x64",
};

import {Binary, Shared, Templates} from "Sdk.Native.Shared";

import * as Cl              from "Sdk.Native.Tools.Cl";
import * as Cnc             from "Sdk.Native.Tools.Cnc";
import * as CPreprocessor   from "Sdk.Native.Tools.CPreprocessor";
import * as CvtRes          from "Sdk.Native.Tools.CvtRes";
import * as Dll             from "Sdk.Native.Tools.Dll";
import * as EtwManifest     from "Sdk.Native.Tools.EtwManifest";
import * as Exe             from "Sdk.Native.Tools.Exe";
import * as Lib             from "Sdk.Native.Tools.Lib";
import * as Link            from "Sdk.Native.Tools.Link";
import * as Mc              from "Sdk.Native.Tools.Mc";
import * as Midl            from "Sdk.Native.Tools.Midl";
import * as Msxsl           from "Sdk.Native.Tools.Msxsl";
import * as Perl            from "Sdk.Native.Tools.Perl";
import * as Rc              from "Sdk.Native.Tools.Rc";
import * as StaticLibrary   from "Sdk.Native.Tools.StaticLibrary";
import * as Wpp             from "Sdk.Native.Tools.Wpp";

// TODO: Temporary workaround to maintain compatibility with OSGTools
import {Shared as Core} from "Sdk.Native.Shared";

@@public
export {
    Core,
    Shared,
    Templates,
    Binary,
    Dll,
    Exe,
    StaticLibrary,
    Cl,
    Cnc,
    CPreprocessor,
    CvtRes,
    EtwManifest,
    Lib,
    Link,
    Mc,
    Midl,
    Msxsl,
    Perl,
    Rc,
    Wpp,
};
