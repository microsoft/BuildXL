// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

'use strict';

// This must be the first statement otherwise modules might got loaded with
// the wrong locale.
import * as nls from 'vscode-nls';
import { env, ExtensionContext, commands, window, Uri, WorkspaceEdit, Range, workspace } from 'vscode';
nls.config({ locale: env.language });

import { RequestType, TextEdit, LanguageClient } from 'vscode-languageclient';
import { DominoSpecFileTreeItem } from './projectBrowser';
import * as path from 'path';

/**
 * Represents the parameters for the "dscript/addSourceFile" JSON-RPC request.
 */
interface AddSourceFileToProjectParams {
    /**
     * The project spec file that will have the source file added to it.
     */
    projectSpecFileName: string;

    /**
     * The relateive source file path to add the project.
     */
    relativeSourceFilePath: string;
}

/**
 *  Create the JSON-RPC request object for retrieving the modules present in the DScript workspace.
 */
const AddSourceFileToProjectRequest = new RequestType<AddSourceFileToProjectParams, TextEdit[], any, any>("dscript/addSourceFileToProject");

export function configureAddSourceFile(languageClient: LanguageClient, extensionContext: ExtensionContext) {
    // Register the add source file command.
    extensionContext.subscriptions.push(commands.registerCommand('DScript.addSourceFileToProject', (specItem: (DominoSpecFileTreeItem | Uri)) => {
        if (specItem instanceof Uri) {
            addSourceFile(languageClient, specItem);
            return;
        }
        return addSourceFile(languageClient, specItem.uri());
    }));
}

function addSourceFile(languageClient : LanguageClient, uri: Uri): void {
    window.showOpenDialog({
        canSelectFiles: true,
        canSelectFolders: false,
        canSelectMany: false,
        openLabel: "Add",
        defaultUri: uri,
        filters: {
            "C/C++ Files" : ["c", "cxx", "cpp"],
            "C# Files" : ["cs"],
            "All Files" : ["*"],
        }
    }).then((result: Uri[]) => {
        if (result && result.length === 1) {
            const projectPath = path.dirname(uri.fsPath);
            const relativePath = path.relative(projectPath, result[0].fsPath);

            // path.relative will simply return the "to" path if they cannot be
            // made relative.
            if (relativePath === result[0].fsPath) {
                window.showWarningMessage("File must be relative to project path: " + projectPath);
                return;
            }

            languageClient.sendRequest(AddSourceFileToProjectRequest, <AddSourceFileToProjectParams>{ projectSpecFileName: uri.fsPath, relativeSourceFilePath: relativePath }).then(edits => {
                var we = new  WorkspaceEdit();
                we.replace(uri, new Range(edits[0].range.start.line, edits[0].range.start.character, edits[0].range.end.line, edits[0].range.end.character), edits[0].newText);
                workspace.applyEdit(we);
            });
        }
    });
}
