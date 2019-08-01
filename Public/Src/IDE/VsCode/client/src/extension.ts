// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

'use strict';

// This must be the first statement otherwise modules might got loaded with
// the wrong locale.
import * as nls from 'vscode-nls';
import { EventEmitter, TreeItem, TreeDataProvider, extensions, languages, commands, window, env, workspace, Disposable, ExtensionContext, IndentAction, Uri, Selection, Range, ViewColumn, Position, StatusBarAlignment, StatusBarItem, TreeItemCollapsibleState, SnippetString, QuickDiffProvider, QuickPickItem} from 'vscode';

nls.config({ locale: env.language });

import * as path from 'path';
import * as fs from 'fs';

import { checkForUpdates } from './update';
import { RequestType, LanguageClient, LanguageClientOptions, ServerOptions, TransportKind } from 'vscode-languageclient';
import { OutputTracer } from './notifications/outputTracer';
import { LogFileLocationNotification } from './notifications/logFileNotification';
import { WorkspaceLoadingNotification } from './notifications/workspaceLoadingNotification';
import { FindReferenceNotification } from './notifications/findReferenceNotification';
import { createDominoProjectBrowser } from './ProjectManagement/projectBrowser';

var languageClient : LanguageClient = undefined;
var extensionContext : ExtensionContext = undefined;
var createdProjectBrowser : boolean = false;

const enum ClientType
{
    Unknown,
    VisualStudioCode,
    VisualStudio,
}

export function activate(context: ExtensionContext) {
    extensionContext = context;

    // Check to see if we have extension updates available.
    checkForUpdates();

    let exeName = 'BuildXL.Ide.LanguageServer';
    if (process.platform === "win32") exeName = exeName + ".exe";

    // If the extension is launched in debug mode then the debug server options are used
    // Otherwise the run options are used
    const serverOptions: ServerOptions = {
        run: {
            module: "DScript Language Server",
            transport: TransportKind.pipe,
            runtime: context.asAbsolutePath(path.join('./bin', exeName)),
        },
        debug: {
            module: "DScript Language Server",
            transport: TransportKind.pipe,
            runtime: context.asAbsolutePath(`../../../../../Out/objects/tempdeployment/debug/netcoreapp3.0/win-x64/VsCodeVsix/extension/bin/${exeName}`),
        },
    }
    
    // Options to control the language client
    const clientOptions: LanguageClientOptions = {
        // Register the server for plain text documents
        documentSelector: ['DScript'],
        synchronize: {
            // Synchronize the setting section 'DScript' to the server
            configurationSection: 'DScript',
            // Notify the server about file changes to '.clientrc files contain in the workspace
            fileEvents: workspace.createFileSystemWatcher('**/.clientrc')
        },
        initializationOptions: {
            clientType: ClientType.VisualStudioCode
        }
    }
    
    // Create the language client
    languageClient = new LanguageClient('DScriptLanguageClient', 'BuildXL DScript Language Client', serverOptions, clientOptions);

    // Set up our "back channel" RPC messags
    languageClient.onReady().then(() => {
        languageClient.onNotification(WorkspaceLoadingNotification.type, WorkspaceLoadingNotification.handler);
        languageClient.onNotification(LogFileLocationNotification.type, LogFileLocationNotification.handler);
        languageClient.onNotification(OutputTracer.type, OutputTracer.handler);

        // Need to set-up tracer explicitely to enable push notifications into the output window.
        OutputTracer.setUpTracer(languageClient);
    });

    // Now start the client
    let languageServer = languageClient.start();

    context.subscriptions.push(commands.registerCommand('DScript.reloadWorkspace', () => {
        return reloadWorkspace();
    }));

    context.subscriptions.push(commands.registerCommand('DScript.openLogFile', () => {
        return openLogFile();
    }));

    // Set the context of the workspace loaded to be false.
    commands.executeCommand('setContext', 'DScript.workspaceLoaded', false);

    context.subscriptions.push(commands.registerCommand('DScript.openDocument', (uriString: string, range?: Range) => {
        return openDocument(uriString, range);
    }));

    // Set up the BuildXL script project browser.
    createDominoProjectBrowser(languageClient, context);

    // Register for configuration changes so we can create\enable the
    // BuildXL project browser when the user changes their configuration.
    workspace.onDidChangeConfiguration(() => {
        // Set up the DScript project browser.
        createDominoProjectBrowser(languageClient, context);
    });
    
    // Register language configuration
    registerLanguageConfiguration();
}

// this method is called when your extension is deactivated
export function deactivate() {
    return languageClient.stop();  
}

function reloadWorkspace(): void {
    commands.executeCommand("reloadWorkspace");
}

function openLogFile(): void {
    if (!LogFileLocationNotification.tryOpenLogFile()) {
        commands.executeCommand("openLogFile");        
    }
}

function openDocument(uriString: string, range?: Range): void {
    const uri = Uri.parse(uriString);

    var textDocument = workspace.openTextDocument(uri).then((document) => {
        window.showTextDocument(document).then((editor) => {
            if (range) {
                editor.selection = new Selection(range.start.line, range.start.character, range.end.line, range.end.character);
                editor.revealRange(range);
            }
        });
    });
}

function registerLanguageConfiguration(): void {
    languages.setLanguageConfiguration('DScript', {
        indentationRules: {
            // ^(.*\*/)?\s*\}.*$
            decreaseIndentPattern: /^(.*\*\/)?\s*\}.*$/,
            // ^.*\{[^}"']*$
            increaseIndentPattern: /^.*\{[^}"']*$/
        },
        wordPattern: /(-?\d*\.\d\w*)|([^\`\~\!\@\#\%\^\&\*\(\)\-\=\+\[\{\]\}\\\|\;\:\'\"\,\.\<\>\/\?\s]+)/g,
        comments: {
            lineComment: '//',
            blockComment: ['/*', '*/']
        },
        brackets: [
            ['{', '}'],
            ['[', ']'],
            ['(', ')'],
        ],
        onEnterRules: [
            {
                // e.g. /** | */
                beforeText: /^\s*\/\*\*(?!\/)([^\*]|\*(?!\/))*$/,
                afterText: /^\s*\*\/$/,
                action: { indentAction: IndentAction.IndentOutdent, appendText: ' * ' }
            },
            {
                // e.g. /** ...|
                beforeText: /^\s*\/\*\*(?!\/)([^\*]|\*(?!\/))*$/,
                action: { indentAction: IndentAction.None, appendText: ' * ' }
            },
            {
                // e.g.  * ...|
                beforeText: /^(\t|(\ \ ))*\ \*(\ ([^\*]|\*(?!\/))*)?$/,
                action: { indentAction: IndentAction.None, appendText: '* ' }
            },
            {
                // e.g.  */|
                beforeText: /^(\t|(\ \ ))*\ \*\/\s*$/,
                action: { indentAction: IndentAction.None, removeText: 1 }
            }
        ],

        __electricCharacterSupport: {
            docComment: { scope: 'comment.documentation', open: '/**', lineStart: ' * ', close: ' */' }
        },

        __characterPairSupport: {
            autoClosingPairs: [
                { open: '{', close: '}' },
                { open: '[', close: ']' },
                { open: '(', close: ')' },
                { open: '"', close: '"', notIn: ['string'] },
                { open: '\'', close: '\'', notIn: ['string', 'comment'] },
                { open: '`', close: '`', notIn: ['string', 'comment'] }
            ]
        }
    });
}
