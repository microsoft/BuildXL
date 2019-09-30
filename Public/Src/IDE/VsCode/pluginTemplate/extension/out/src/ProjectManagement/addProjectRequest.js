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
function findClosestProjectListFile(projectFileName) {
    return new Promise((resolve, reject) => {
        let projectDirname = path.dirname(projectFileName);
        if (projectDirname === projectFileName) {
            reject("Cannot locate project list file");
        }
        fs.readdir(projectDirname, (err, files) => {
            if (err) {
                reject(err);
                return;
            }
            const foundBuildListFile = files.some((filename, index) => {
                if (path.extname(filename).toLowerCase() === ".bl") {
                    resolve(path.join(projectDirname, filename));
                    return true;
                }
                return false;
            });
            if (!foundBuildListFile) {
                findClosestProjectListFile(projectDirname).then((buildListFile) => {
                    resolve(buildListFile);
                }).catch((error) => {
                    reject(error);
                });
            }
        });
    });
}
/**
 *  Create the JSON-RPC request object for retrieving the modules present in the DScript workspace.
 */
const AddProjectRequest = new vscode_languageclient_1.RequestType("dscript/addProject");
/**
 * Called by the DScript.addProject command which is invoked
 * from the context menu in the project browser.
 * @param item The tree item that represents the module.
 */
function addProject(item, languageClient, extensionContext) {
    const userOverridePath = path.join(extensionContext.storagePath, "addProjectConfigurations.json");
    const addProjectConfigurationFile = fs.existsSync(userOverridePath) ? userOverridePath : extensionContext.asAbsolutePath("ProjectManagement\\addProjectConfigurations.json");
    let addProjectConfigurations;
    try {
        addProjectConfigurations = require(addProjectConfigurationFile);
    }
    catch (_a) {
        vscode_1.window.showErrorMessage(`The add project configuration file '${addProjectConfigurationFile}' cannot be parsed.`);
    }
    const quickPickItems = addProjectConfigurations.configurations.map(configuration => {
        return {
            description: configuration.description,
            label: configuration.quickPickLabel,
            snippet: configuration.snippet
        };
    });
    // As the user which project type.
    vscode_1.window.showQuickPick(quickPickItems, { placeHolder: "Choose project type to create" }).then((pickedItem) => {
        if (!pickedItem) {
            return;
        }
        const quickPickItem = pickedItem;
        const projectBaseUri = path.dirname(item.descriptor().configFilename).toLowerCase();
        const openDialogUri = vscode_1.Uri.parse("file:///" + projectBaseUri);
        // Let the user pick a destination folder based on the module configuration directory.
        vscode_1.window.showOpenDialog({
            canSelectFiles: false,
            canSelectFolders: true,
            canSelectMany: false,
            defaultUri: openDialogUri,
            openLabel: "Select folder"
        }).then((folders) => {
            // This will return undefined on cancel and we only support one file.
            if (folders && folders.length === 1) {
                let newProjectFilePath = path.join(folders[0].fsPath, "sources.bp").toLowerCase();
                if (!newProjectFilePath.startsWith(projectBaseUri)) {
                    vscode_1.window.showWarningMessage("Project must be under module configuration path: " + projectBaseUri);
                    return;
                }
                // Don't overwrite a file if it already exists.
                if (fs.existsSync(newProjectFilePath)) {
                    vscode_1.window.showWarningMessage("Project file already exists at: " + newProjectFilePath);
                    return;
                }
                // Create a new file with the untitled "schema". This
                // causes VSCode to create the file in the editor but
                // does not commit it do disk.
                let newUri = vscode_1.Uri.parse("untitled:" + newProjectFilePath);
                vscode_1.workspace.openTextDocument(newUri).then((textDoc) => {
                    vscode_1.window.showTextDocument(textDoc).then((editor) => {
                        const totalSnippet = quickPickItem.snippet.join("\r\n");
                        editor.insertSnippet(new vscode_1.SnippetString(totalSnippet)).then(() => {
                            // This can probably be done in the Language Server itself.
                            findClosestProjectListFile(newProjectFilePath).then((buildListPath) => {
                                languageClient.sendRequest(AddProjectRequest, {
                                    moduleFilename: item.descriptor().configFilename,
                                    projectFilename: newProjectFilePath
                                });
                            });
                        });
                    });
                });
            }
        });
    });
}
function configureAddProject(languageClient, extensionContext) {
    extensionContext.subscriptions.push(vscode_1.commands.registerCommand('DScript.addProject', (item) => {
        return addProject(item, languageClient, extensionContext);
    }));
}
exports.configureAddProject = configureAddProject;
//# sourceMappingURL=addProjectRequest.js.map