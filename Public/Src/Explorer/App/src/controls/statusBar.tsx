// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as React from 'react';
import * as Status from '../models/status';
import { Panel, PanelType } from 'office-ui-fabric-react';

export interface StatusState {
    message: Status.LoggedMessage | undefined,
    showPanel: boolean,
    messages: Status.LoggedMessage[],
}

export class StatusBar extends React.Component<{}, StatusState> {

    public constructor(props: {}) {
        super(props);

        this.state = {
            message: Status.status.lastMessage,
            showPanel: false,
            messages: []
        }

        window.addEventListener("bxp-messageLogged", (e : CustomEvent<Status.LoggedMessage>) => {
            this.setState({message: e.detail});
        });
    }

    private _showPanel = (): void => {
        this.setState({ showPanel: true });
    };

    private _onRenderMessages = (): JSX.Element => {
        return (
            <div>
                {
                    Status.status.messages.map(m => (<div className="status-line">{m.message}</div>))
                }
            </div>
        )
    }

    render() {
        return (
            <div className="statusbar">
                <span className="status-count">{Status.status.messages.length}</span>
                <span className="status-msg">{Status.status.lastMessage ? Status.status.lastMessage.message : ""}</span>
                <i className="status-show ms-Icon ms-Icon--GoToMessage" title="Show all messages" onClick={this._showPanel}></i>
                <Panel
                    isOpen={this.state.showPanel}
                    onDismiss={() => this.setState({ showPanel: false })}
                    type={PanelType.extraLarge}
                    headerText="Log messages"
                    onRenderBody={this._onRenderMessages}
                    >
                    <span>Content goes here.</span>
                </Panel>
            </div>
        );
    }

}
