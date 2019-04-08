// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import * as Deployment from "Sdk.Deployment";

// Copy tools sub-directory of QTest package into BuildXL Bin directory
const qTestToolsStaticDirectory = (() => {
    const qTestStaticDirectory = importFrom("CB.QTest").Contents.all;
    const qTestToolsDirectory = d`${qTestStaticDirectory.root}/tools`;
    const v150DirectoryPath = p`${qTestToolsDirectory}/V150`;
    const vsTestConsoleDirectoryPath = p`${qTestToolsDirectory}/VsTestConsole`;
    const toolsFiles = qTestStaticDirectory.contents.filter(
        f => f.parent === qTestToolsDirectory.path || f.isWithin(v150DirectoryPath) || f.isWithin(vsTestConsoleDirectoryPath)
    );
    return Transformer.sealPartialDirectory(qTestToolsDirectory, toolsFiles);
}
)();

@@public
export const deployment: Deployment.Definition = {
    contents: [
        f`Tool.QTestRunner.dsc`,
        {file: f`LiteralFiles/package.config.dsc.literal`, targetFileName: a`package.config.dsc`},
        {subfolder: "bin", contents: [qTestToolsStaticDirectory]},
    ],
};
