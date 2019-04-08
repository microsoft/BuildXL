// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
'use strict';
Object.defineProperty(exports, "__esModule", { value: true });
const vscode_1 = require("vscode");
// This must be the first statement otherwise modules might got loaded with
// the wrong locale.
const nls = require("vscode-nls");
nls.config({ locale: vscode_1.env.language });
const vscode_jsonrpc_1 = require("vscode-jsonrpc");
/**
 * Contains the functionality for the workspace loading progress
 * sent from the language server over the JSON RPC back channel.
 *
 * CODESYNC: The enumerations must be kept in sync with the language server implementation.
 * public\src\ide\languageserver\jsonrpc\workspaceloadingparams.cs
 */
var WorkspaceLoadingNotification;
(function (WorkspaceLoadingNotification) {
    WorkspaceLoadingNotification.WorkspaceLoadingEvent = new vscode_1.EventEmitter();
    /**
     * The notification type (the method name, the parameter type, and return type) used
     * to configure the JSON RPC layer.
     */
    WorkspaceLoadingNotification.type = new vscode_jsonrpc_1.NotificationType(`dscript/workspaceLoading`);
    /**
     * The status bar item that is created to show the workspace loading status.
     */
    var workspaceLoadingStatusBarItem = undefined;
    function createProgressString(params) {
        // Using ifDefined but not "||" because '0' is falsy.
        return `${ifDefined(params.numberOfProcessedSpecs, "0")}/${ifDefined(params.totalNumberOfSpecs, "?")}`;
    }
    function ifDefined(value, ifNotDefinedValue) {
        if (value === undefined || value === null) {
            return ifNotDefinedValue;
        }
        return value;
    }
    /**
     * The handler called by the JSON RPC layer to process the notification.
     */
    function handler(params) {
        if (workspaceLoadingStatusBarItem === undefined) {
            workspaceLoadingStatusBarItem = vscode_1.window.createStatusBarItem(vscode_1.StatusBarAlignment.Left);
            workspaceLoadingStatusBarItem.command = "DScript.openLogFile";
            workspaceLoadingStatusBarItem.show();
        }
        let progressString = undefined;
        WorkspaceLoadingNotification.WorkspaceLoadingEvent.fire(params.status);
        switch (params.status) {
            case 0 /* Init */:
                progressString = `Initializing...`;
                break;
            case 1 /* InProgress */:
                switch (params.progressStage) {
                    case 0 /* BuildingWorkspaceDefinition */:
                        progressString = `Processing configuration files ${ifDefined(params.numberOfProcessedSpecs, '...')}`;
                        break;
                    case 1 /* Parse */:
                        progressString = `Parsing specs ${createProgressString(params)}`;
                        break;
                    case 2 /* Analysis */:
                        progressString = `Analyzing specs ${createProgressString(params)}`;
                        break;
                    case 3 /* Conversion */:
                        progressString = `Converting specs ${createProgressString(params)}`;
                        break;
                }
                workspaceLoadingStatusBarItem.text =
                    progressString ? `$(sync) Loading workspace: ${progressString}` : `$(sync) Loading workspace`;
                break;
            case 2 /* Success */:
                workspaceLoadingStatusBarItem.text = "$(check) Workspace loading complete";
                setTimeout(() => {
                    workspaceLoadingStatusBarItem.hide();
                    workspaceLoadingStatusBarItem.dispose();
                    workspaceLoadingStatusBarItem = undefined;
                }, 5000);
                // Enable the add-file context menu option
                vscode_1.commands.executeCommand('setContext', 'DScript.workspaceLoaded', true);
                break;
            case 3 /* Failure */:
                workspaceLoadingStatusBarItem.text = "$(stop) Workspace loading failed ";
                break;
        }
    }
    WorkspaceLoadingNotification.handler = handler;
})(WorkspaceLoadingNotification = exports.WorkspaceLoadingNotification || (exports.WorkspaceLoadingNotification = {}));
//# sourceMappingURL=workspaceLoadingNotification.js.map