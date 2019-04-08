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
/**
 *  Create the JSON-RPC request object for retrieving the modules present in the DScript workspace.
 */
const AddSourceFileToProjectRequest = new vscode_languageclient_1.RequestType("dscript/addSourceFileToProject");
function configureAddSourceFile(languageClient, extensionContext) {
    // Register the add source file command.
    extensionContext.subscriptions.push(vscode_1.commands.registerCommand('DScript.addSourceFileToProject', (specItem) => {
        if (specItem instanceof vscode_1.Uri) {
            addSourceFile(languageClient, specItem);
            return;
        }
        return addSourceFile(languageClient, specItem.uri());
    }));
}
exports.configureAddSourceFile = configureAddSourceFile;
function addSourceFile(languageClient, uri) {
    vscode_1.window.showOpenDialog({
        canSelectFiles: true,
        canSelectFolders: false,
        canSelectMany: false,
        openLabel: "Add",
        defaultUri: uri,
        filters: {
            "C/C++ Files": ["c", "cxx", "cpp"],
            "C# Files": ["cs"],
            "All Files": ["*"],
        }
    }).then((result) => {
        if (result && result.length === 1) {
            const projectPath = path.dirname(uri.fsPath);
            const relativePath = path.relative(projectPath, result[0].fsPath);
            // path.relative will simply return the "to" path if they cannot be
            // made relative.
            if (relativePath === result[0].fsPath) {
                vscode_1.window.showWarningMessage("File must be relative to project path: " + projectPath);
                return;
            }
            languageClient.sendRequest(AddSourceFileToProjectRequest, { projectSpecFileName: uri.fsPath, relativeSourceFilePath: relativePath }).then(edits => {
                var we = new vscode_1.WorkspaceEdit();
                we.replace(uri, new vscode_1.Range(edits[0].range.start.line, edits[0].range.start.character, edits[0].range.end.line, edits[0].range.end.character), edits[0].newText);
                vscode_1.workspace.applyEdit(we);
            });
        }
    });
}
//# sourceMappingURL=addSourceFileRequest.js.map