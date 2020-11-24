// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import * as Deployment from "Sdk.Deployment";

// Copy tools sub-directory of QTest package into BuildXL Bin directory
const qTestToolsStaticDirectory = (() => {
    const qTestStaticDirectory = importFrom("CB.QTest").Contents.all;
    const qTestToolsDirectory = d`${qTestStaticDirectory.root}/tools`;
    const v150DirectoryPath = p`${qTestToolsDirectory}/V150`;
    const corruptCoverageFileFixerPath = p`${qTestToolsDirectory}/CorruptCoverageFileFixer`;
    const procDumpPath = p`${qTestToolsDirectory}/QTestProcDump`; 
    const toolsFiles = qTestStaticDirectory.contents.filter(
        f => f.parent === qTestToolsDirectory.path || f.isWithin(v150DirectoryPath) || f.isWithin(corruptCoverageFileFixerPath) || f.isWithin(procDumpPath)
    );
    return Transformer.sealPartialDirectory(qTestToolsDirectory, toolsFiles);
}
)();

const specs = [
    f`Tool.QTestRunner.dsc`,
    {file: f`LiteralFiles/package.config.dsc.literal`, targetFileName: a`package.config.dsc`},
];

@@public
export const deployment: Deployment.Definition = {
    contents: [
        ...specs,
        {subfolder: "bin", contents: [qTestToolsStaticDirectory]},
    ],
};

@@public
export const evaluationOnlyDeployment: Deployment.Definition = {
    contents: specs
};

@@public
export function selectDeployment(evaluationOnly: boolean) : Deployment.Definition {
    return evaluationOnly? evaluationOnlyDeployment : deployment;
}
