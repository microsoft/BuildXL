// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

'use strict';

// This must be the first statement otherwise modules might got loaded with
// the wrong locale.
import * as nls from 'vscode-nls';
import { env, SnippetString, QuickPickItem, ExtensionContext, commands, window, Uri, WorkspaceEdit, Range, workspace } from 'vscode';
nls.config({ locale: env.language });

import { RequestType, TextEdit, LanguageClient } from 'vscode-languageclient';
import { DominoModuleTreeItem } from './projectBrowser';
import * as path from 'path';
import * as fs from 'fs';
import { config } from 'vscode-nls';

/**
 * Base class used to represent the project types preseented
 * to the user when they want to create a new file/
 */
interface DominoProjectQuickPickItem extends QuickPickItem {
    
    /**
     * Represents the snippet resource filename to create when
     * the user chooses this project type/
     */
    snippet: string[];
}

function findClosestProjectListFile(projectFileName: string) : Promise<string> {
    return new Promise<string>((resolve, reject) =>  {
        let projectDirname = path.dirname(projectFileName);
        if (projectDirname === projectFileName) {
            reject("Cannot locate project list file");
        }

        fs.readdir(projectDirname, (err, files) => {
            if (err) {
                reject(err);
                return;
            }

            const foundBuildListFile = files.some((filename, index) =>{
                if (path.extname(filename).toLowerCase() === ".bl") {
                    resolve(path.join(projectDirname, filename));
                    return true;
                }
                return false;
            });
            
            if (!foundBuildListFile){
                findClosestProjectListFile(projectDirname).then( (buildListFile) => {
                    resolve(buildListFile);
                }).catch((error) => {
                    reject(error);
                });
            }
        });    
    });
}

interface AddProjectRequestParams {
    moduleFilename: string;
    projectFilename: string;
}

/**
 *  Create the JSON-RPC request object for retrieving the modules present in the DScript workspace.
 */
const AddProjectRequest = new RequestType<AddProjectRequestParams, any, any, any>("dscript/addProject");

interface AddProjectConfiguration {
    quickPickLabel: string;
    description: string;
    snippet: string[];
}

interface AddProjectConfigurations {
    configurations: AddProjectConfiguration[];
}

/**
 * Called by the DScript.addProject command which is invoked
 * from the context menu in the project browser.
 * @param item The tree item that represents the module.
 */
function addProject(item: DominoModuleTreeItem, languageClient: LanguageClient, extensionContext : ExtensionContext) : void {
    const userOverridePath = path.join(extensionContext.storagePath, "addProjectConfigurations.json");
    const addProjectConfigurationFile = fs.existsSync(userOverridePath) ? userOverridePath : extensionContext.asAbsolutePath("ProjectManagement\\addProjectConfigurations.json");

    let addProjectConfigurations: AddProjectConfigurations;
    try {
        addProjectConfigurations = <AddProjectConfigurations>require(addProjectConfigurationFile);
    } catch {
        window.showErrorMessage(`The add project configuration file '${addProjectConfigurationFile}' cannot be parsed.` )
    }

    const quickPickItems = addProjectConfigurations.configurations.map<DominoProjectQuickPickItem>(configuration => {
        return <DominoProjectQuickPickItem>{
            description: configuration.description,
            label: configuration.quickPickLabel,
            snippet: configuration.snippet
        }
    });
    // As the user which project type.
    window.showQuickPick(quickPickItems, { placeHolder: "Choose project type to create" }).then( (pickedItem) => {

        if (!pickedItem) {
            return;
        }
                    
        const quickPickItem = pickedItem as DominoProjectQuickPickItem;
        const projectBaseUri = path.dirname(item.descriptor().configFilename).toLowerCase();
        const openDialogUri = Uri.parse("file:///" + projectBaseUri);

        // Let the user pick a destination folder based on the module configuration directory.
        window.showOpenDialog({
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
                    window.showWarningMessage("Project must be under module configuration path: " + projectBaseUri);
                    return;
                }

                // Don't overwrite a file if it already exists.
                if (fs.existsSync(newProjectFilePath)) {
                    window.showWarningMessage("Project file already exists at: " + newProjectFilePath);
                    return;
                }

                // Create a new file with the untitled "schema". This
                // causes VSCode to create the file in the editor but
                // does not commit it do disk.
                let newUri : Uri = Uri.parse("untitled:" + newProjectFilePath);

                workspace.openTextDocument(newUri).then((textDoc) => {
                    window.showTextDocument(textDoc).then((editor) => {
                        const totalSnippet = quickPickItem.snippet.join("\r\n");

                        editor.insertSnippet(new SnippetString(totalSnippet)).then(()=> {
                            // This can probably be done in the Language Server itself.
                            findClosestProjectListFile(newProjectFilePath).then((buildListPath) => {
                                languageClient.sendRequest(AddProjectRequest, <AddProjectRequestParams>{
                                    moduleFilename: item.descriptor().configFilename,
                                    projectFilename: newProjectFilePath
                                });
                            });
                        })
                    })
                });
            }
        })
    });
}

export function configureAddProject(languageClient: LanguageClient, extensionContext: ExtensionContext) {
    extensionContext.subscriptions.push(commands.registerCommand('DScript.addProject', (item: DominoModuleTreeItem) => {
        return addProject(item, languageClient, extensionContext);
    }));
}
