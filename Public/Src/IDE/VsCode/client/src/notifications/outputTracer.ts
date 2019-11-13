// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

'use strict';

import { window, env, Uri, StatusBarItem, StatusBarAlignment } from 'vscode';

// This must be the first statement otherwise modules might got loaded with
// the wrong locale.
import * as nls from 'vscode-nls';
nls.config({ locale: env.language });

import { NotificationType } from 'vscode-jsonrpc';
import { LanguageClient } from 'vscode-languageclient';

/**
 * Contains the functionality for writing crucial information to the output window.
 */
export namespace OutputTracer {
    
    /** Log event level copied from the Microsoft.Diagnostics.Tracing.EventLevel enumeration. */
    export const enum EventLevel {
        LogAlways = 0,
        Critical = 1,
        Error = 2,
        Warning = 3,
        Informational = 4,
        Verbose = 5
    }

    /** Message to show in the output window. */
    export interface LogMessage {
        level: EventLevel;
        message: string;
    }

    /**
     * The notification type (the method name, the parameter type, and return type) used
     * to configure the JSON RPC layer.
     */
    export const type : NotificationType<LogMessage, void> = new NotificationType('dscript/outputTrace');

    let currentClient: LanguageClient;
    
    /** Stores the current client in the global state for handling notification from the language server. */
    export function setUpTracer(client: LanguageClient) {
        currentClient = client;
    }

    /**
     * The handler called by the JSON RPC layer to process the notification.
     */
    export function handler(params: LogMessage) : void {
        if (currentClient && params.level !== undefined && params.message !== undefined) {
            switch(params.level) {
                case EventLevel.Informational:
                case EventLevel.LogAlways:
                case EventLevel.Verbose:
                    currentClient.info(params.message);
                    break;
                case EventLevel.Critical:
                case EventLevel.Error:
                    currentClient.error(params.message);
                    break;
                case EventLevel.Warning:
                    currentClient.warn(params.message);
                    break;
            }
        }
    }
}
