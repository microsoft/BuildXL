// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

'use strict';
Object.defineProperty(exports, "__esModule", { value: true });
// This must be the first statement otherwise modules might got loaded with
// the wrong locale.
const vscode_1 = require("vscode");
const nls = require("vscode-nls");
nls.config({ locale: vscode_1.env.language });
const vscode_languageclient_1 = require("vscode-languageclient");
;
/**
 *  Create the JSON-RPC request object for retrieving the modules present in the DScript workspace.
 */
exports.ModulesForWorkspaceRequest = new vscode_languageclient_1.RequestType("dscript/modulesForWorkspace");
//# sourceMappingURL=modulesForWorkspaceRequest.js.map
