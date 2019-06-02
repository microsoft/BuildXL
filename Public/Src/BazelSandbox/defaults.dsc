
// Copyright 2019 The Bazel Authors. All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
import * as BuildXLSdk from "Sdk.BuildXL";
import {NetFx} from "Sdk.BuildXL";

export {BuildXLSdk, NetFx};
export declare const qualifier: BuildXLSdk.DefaultQualifier;

namespace BazelSandbox {
    export declare const qualifier: BuildXLSdk.DefaultQualifier;
    @@public
    export const exe = BuildXLSdk.executable({
        assemblyName: "BazelSandbox",
        sources: globR(d`.`, "*.cs"),
        
        references: [
            importFrom("BuildXL.Engine").Processes.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Native.dll,
        ],
    });
}
