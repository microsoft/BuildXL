// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

export interface AppState {
    mode: "open" | "build";
    sessionId: string | undefined;
}


let _state : AppState = {
    mode: "open",
    sessionId: undefined,
};

export function getState() : AppState{
    return _state;
}

export function setOpen() {
    setState({
        mode: "open",
        sessionId: undefined,
    });
}

export function setBuild(sessionId: string) {
    setState({
        mode: "build",
        sessionId: sessionId,
    });
}

function setState(state: AppState) {
    if (_state.mode === state.mode && 
        _state.sessionId === state.sessionId)
    {
        return;
    }

    _state = state;
    var event = new CustomEvent("bxp-appStateChanged", { detail: _state });
    window.dispatchEvent(event);
}
