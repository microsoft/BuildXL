// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

'use strict';

import {
    DebugSession,
    InitializedEvent, TerminatedEvent, StoppedEvent, BreakpointEvent, OutputEvent, Event,
    Thread, StackFrame, Scope, Source, Handles, Breakpoint
} from 'vscode-debugadapter';
import {DebugProtocol} from 'vscode-debugprotocol';

interface AttachRequest extends DebugProtocol.AttachRequest {
    port: number;
}

class DominoDebugSession extends DebugSession {
    protected attachRequest(response: DebugProtocol.Response, args: AttachRequest) {
    }
}
