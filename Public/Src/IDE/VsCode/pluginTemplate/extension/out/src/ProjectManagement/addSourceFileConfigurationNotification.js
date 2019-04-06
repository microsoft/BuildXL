// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
'use strict';
Object.defineProperty(exports, "__esModule", { value: true });
// This must be the first statement otherwise modules might got loaded with
// the wrong locale.
const nls = require("vscode-nls");
const vscode_1 = require("vscode");
nls.config({ locale: vscode_1.env.language });
const vscode_languageclient_1 = require("vscode-languageclient");
const path = require("path");
const fs = require("fs");
/**
*  Create the JSON-RPC request object for retrieving the specs present in a module in the DScript workspace.
*/
const AddSourceFileConfigurationNotification = new vscode_languageclient_1.NotificationType("dscript/sourceFileConfiguration");
function sendAddSourceFileConfigurationToLanguageServer(languageClient, extensionContext) {
    // Send the add source notification to the language server
    const userOverridePath = path.join(extensionContext.storagePath, "addSourceFileConfiguration.json");
    const addSourceConfugrationFile = fs.existsSync(userOverridePath) ? userOverridePath : extensionContext.asAbsolutePath("ProjectManagement\\addSourceFileConfiguration.json");
    let addSourceConfugrationParams;
    try {
        addSourceConfugrationParams = require(addSourceConfugrationFile);
    }
    catch (_a) {
        vscode_1.window.showErrorMessage(`The add source configuration file '${addSourceConfugrationFile}' cannot be parsed.`);
    }
    languageClient.sendNotification(AddSourceFileConfigurationNotification, addSourceConfugrationParams);
}
exports.sendAddSourceFileConfigurationToLanguageServer = sendAddSourceFileConfigurationToLanguageServer;
//# sourceMappingURL=addSourceFileConfigurationNotification.js.map