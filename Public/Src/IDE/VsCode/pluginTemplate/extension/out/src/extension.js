// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
'use strict';
Object.defineProperty(exports, "__esModule", { value: true });
// This must be the first statement otherwise modules might got loaded with
// the wrong locale.
const nls = require("vscode-nls");
const vscode_1 = require("vscode");
nls.config({ locale: vscode_1.env.language });
const path = require("path");
const update_1 = require("./update");
const vscode_languageclient_1 = require("vscode-languageclient");
const outputTracer_1 = require("./notifications/outputTracer");
const logFileNotification_1 = require("./notifications/logFileNotification");
const workspaceLoadingNotification_1 = require("./notifications/workspaceLoadingNotification");
const projectBrowser_1 = require("./ProjectManagement/projectBrowser");
const xlgstatus = require("./xlg-status");
var languageClient = undefined;
var extensionContext = undefined;
var createdProjectBrowser = false;
function activate(context) {
    xlgstatus.activate(context);
    extensionContext = context;
    // Check to see if we have extension updates available.
    update_1.checkForUpdates();
    let exeName = 'BuildXL.Ide.LanguageServer';
    if (process.platform === "win32")
        exeName = exeName + ".exe";
    // If the extension is launched in debug mode then the debug server options are used
    // Otherwise the run options are used
    const serverOptions = {
        run: {
            module: "DScript Language Server",
            transport: vscode_languageclient_1.TransportKind.pipe,
            runtime: context.asAbsolutePath(path.join('./bin', exeName)),
        },
        debug: {
            module: "DScript Language Server",
            transport: vscode_languageclient_1.TransportKind.pipe,
            runtime: context.asAbsolutePath(`../../../../../Out/objects/tempdeployment/debug/netcoreapp3.0/win-x64/VsCodeVsix/extension/bin/${exeName}`),
        },
    };
    // Options to control the language client
    const clientOptions = {
        // Register the server for plain text documents
        documentSelector: ['DScript'],
        synchronize: {
            // Synchronize the setting section 'DScript' to the server
            configurationSection: 'DScript',
            // Notify the server about file changes to '.clientrc files contain in the workspace
            fileEvents: vscode_1.workspace.createFileSystemWatcher('**/.clientrc')
        },
        initializationOptions: {
            clientType: 1 /* VisualStudioCode */
        }
    };
    // Create the language client
    languageClient = new vscode_languageclient_1.LanguageClient('DScriptLanguageClient', 'BuildXL DScript Language Client', serverOptions, clientOptions);
    // Set up our "back channel" RPC messags
    languageClient.onReady().then(() => {
        languageClient.onNotification(workspaceLoadingNotification_1.WorkspaceLoadingNotification.type, workspaceLoadingNotification_1.WorkspaceLoadingNotification.handler);
        languageClient.onNotification(logFileNotification_1.LogFileLocationNotification.type, logFileNotification_1.LogFileLocationNotification.handler);
        languageClient.onNotification(outputTracer_1.OutputTracer.type, outputTracer_1.OutputTracer.handler);
        // Need to set-up tracer explicitely to enable push notifications into the output window.
        outputTracer_1.OutputTracer.setUpTracer(languageClient);
    });
    // Now start the client
    let languageServer = languageClient.start();
    context.subscriptions.push(vscode_1.commands.registerCommand('DScript.reloadWorkspace', () => {
        return reloadWorkspace();
    }));
    context.subscriptions.push(vscode_1.commands.registerCommand('DScript.openLogFile', () => {
        return openLogFile();
    }));
    // Set the context of the workspace loaded to be false.
    vscode_1.commands.executeCommand('setContext', 'DScript.workspaceLoaded', false);
    context.subscriptions.push(vscode_1.commands.registerCommand('DScript.openDocument', (uriString, range) => {
        return openDocument(uriString, range);
    }));
    // Set up the BuildXL script project browser.
    projectBrowser_1.createDominoProjectBrowser(languageClient, context);
    // Register for configuration changes so we can create\enable the
    // BuildXL project browser when the user changes their configuration.
    vscode_1.workspace.onDidChangeConfiguration(() => {
        // Set up the DScript project browser.
        projectBrowser_1.createDominoProjectBrowser(languageClient, context);
    });
    // Register language configuration
    registerLanguageConfiguration();
}
exports.activate = activate;
// this method is called when your extension is deactivated
function deactivate() {
    return languageClient.stop();
}
exports.deactivate = deactivate;
function reloadWorkspace() {
    vscode_1.commands.executeCommand("reloadWorkspace");
}
function openLogFile() {
    if (!logFileNotification_1.LogFileLocationNotification.tryOpenLogFile()) {
        vscode_1.commands.executeCommand("openLogFile");
    }
}
function openDocument(uriString, range) {
    const uri = vscode_1.Uri.parse(uriString);
    var textDocument = vscode_1.workspace.openTextDocument(uri).then((document) => {
        vscode_1.window.showTextDocument(document).then((editor) => {
            if (range) {
                editor.selection = new vscode_1.Selection(range.start.line, range.start.character, range.end.line, range.end.character);
                editor.revealRange(range);
            }
        });
    });
}
function registerLanguageConfiguration() {
    vscode_1.languages.setLanguageConfiguration('DScript', {
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
                action: { indentAction: vscode_1.IndentAction.IndentOutdent, appendText: ' * ' }
            },
            {
                // e.g. /** ...|
                beforeText: /^\s*\/\*\*(?!\/)([^\*]|\*(?!\/))*$/,
                action: { indentAction: vscode_1.IndentAction.None, appendText: ' * ' }
            },
            {
                // e.g.  * ...|
                beforeText: /^(\t|(\ \ ))*\ \*(\ ([^\*]|\*(?!\/))*)?$/,
                action: { indentAction: vscode_1.IndentAction.None, appendText: '* ' }
            },
            {
                // e.g.  */|
                beforeText: /^(\t|(\ \ ))*\ \*\/\s*$/,
                action: { indentAction: vscode_1.IndentAction.None, removeText: 1 }
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
//# sourceMappingURL=extension.js.map