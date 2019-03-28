// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

'use strict';

import { EventEmitter, commands, window, env, Uri, StatusBarItem, StatusBarAlignment } from 'vscode';

// This must be the first statement otherwise modules might got loaded with
// the wrong locale.
import * as nls from 'vscode-nls';
nls.config({ locale: env.language });

import { NotificationType } from 'vscode-jsonrpc';

/**
 * Contains the functionality for the workspace loading progress
 * sent from the language server over the JSON RPC back channel.
 * 
 * CODESYNC: The enumerations must be kept in sync with the language server implementation.
 * public\src\ide\languageserver\jsonrpc\workspaceloadingparams.cs
 */
export namespace WorkspaceLoadingNotification {

    /**
     * Represents the workspace loading state the language server application is currently in.
     */
    export const enum WorkspaceLoadingState {
        /**
         * Workspace loading is about to start.
         */
        Init = 0,

        /**
         * Workspace loading is in progress.
         */
        InProgress,

        /**
         * Workspace loading has succeeded.
         */
        Success,

        /**
         * Workspace loading has failed.
         */
        Failure
    }

    export const WorkspaceLoadingEvent = new EventEmitter<WorkspaceLoadingState>();

    /**
     * The workspace loading progress as known by the BuildXL Engine.
     * This must be kept in sync with the BuildXL progress object in
     * Public\Src\FrontEnd\Core\WorkspaceProgressEventArgs.cs
     */
    export const enum ProgressStage {
        /**
         * Indicates that the workspace definition is being computed.
         */
        BuildingWorkspaceDefinition = 0,


        /**
         * Indicates that the specs are being parsed.
         */
        Parse,

        /**
         * Indicates the specs are being analyzed.
         */
        Analysis,

        /**
         * Indicates the specs are being converted.
         */
        Conversion,
    }

    /**
     * Represents the workspace loading parameters object sent on
     * the JSON Rpc back channel to our extension.
     */
    interface WorkspaceLoadingParams {
        /**
         * The overall current workspace loading status the language server is in.
         */
        status: WorkspaceLoadingState;

        /**
         * The current workspace loading progress is known by BuildXL.
         */
        progressStage: ProgressStage;

        /**
         * The number of specs that have been processed.
         * Only valid when @see ProgressStage is not @see ProgressStage.BuildingWorkspaceDefinition
         */
        numberOfProcessedSpecs?: number;

        /**
         * The total number of specs that will be processed.
         * Only valid when @see ProgressStage is not @see ProgressStage.BuildingWorkspaceDefinition
         */
        totalNumberOfSpecs?: number;
    }

    /**
     * The notification type (the method name, the parameter type, and return type) used
     * to configure the JSON RPC layer.
     */
    export const type : NotificationType<WorkspaceLoadingParams, void> = new NotificationType(`dscript/workspaceLoading`);
    
    /**
     * The status bar item that is created to show the workspace loading status.
     */
    var workspaceLoadingStatusBarItem : StatusBarItem = undefined;
    
    function createProgressString(params: WorkspaceLoadingParams) : string {
        // Using ifDefined but not "||" because '0' is falsy.
        return `${ifDefined(params.numberOfProcessedSpecs, "0")}/${ifDefined(params.totalNumberOfSpecs, "?")}`;
    }

    function ifDefined<T, U>(value: (T | undefined | null), ifNotDefinedValue: U): U | T {
        if (value === undefined || value === null) {
            return ifNotDefinedValue;
        }

        return value;
    }

    /**
     * The handler called by the JSON RPC layer to process the notification.
     */
    export function handler(params: WorkspaceLoadingParams) : void {
        if (workspaceLoadingStatusBarItem === undefined) {
            workspaceLoadingStatusBarItem = window.createStatusBarItem(StatusBarAlignment.Left);
            workspaceLoadingStatusBarItem.command = "DScript.openLogFile";
            workspaceLoadingStatusBarItem.show();
        }
        
        let progressString = undefined;

        WorkspaceLoadingEvent.fire(params.status);

        switch (params.status) {
            case WorkspaceLoadingState.Init:
                progressString = `Initializing...`;
            break;

            case WorkspaceLoadingState.InProgress:
                switch (params.progressStage) {
                    case ProgressStage.BuildingWorkspaceDefinition:
                        progressString = `Processing configuration files ${ifDefined(params.numberOfProcessedSpecs, '...')}`;
                        break;
                    case ProgressStage.Parse:
                        progressString = `Parsing specs ${createProgressString(params)}`;
                        break;
                    case ProgressStage.Analysis:
                        progressString = `Analyzing specs ${createProgressString(params)}`;
                        break;
                    case ProgressStage.Conversion:
                        progressString = `Converting specs ${createProgressString(params)}`;
                        break;
                }

                workspaceLoadingStatusBarItem.text = 
                    progressString ? `$(sync) Loading workspace: ${progressString}` : `$(sync) Loading workspace`;
                break;

            case WorkspaceLoadingState.Success:
                workspaceLoadingStatusBarItem.text = "$(check) Workspace loading complete";
                setTimeout( () => {
                    workspaceLoadingStatusBarItem.hide();
                    workspaceLoadingStatusBarItem.dispose();
                    workspaceLoadingStatusBarItem = undefined;
                }, 5000);

                // Enable the add-file context menu option
                commands.executeCommand('setContext', 'DScript.workspaceLoaded', true);
                break;

            case WorkspaceLoadingState.Failure:
                workspaceLoadingStatusBarItem.text = "$(stop) Workspace loading failed ";
                break;
        }
    }
}
