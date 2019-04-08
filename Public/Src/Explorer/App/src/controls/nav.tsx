// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as React from 'react';
import { NavLink } from 'react-router-dom'
import * as AppState from "../models/appState"

interface State {
    appState: AppState.AppState;
}

export class Nav extends React.Component<{}, State> {
    public constructor(props: {}) {
        super(props);

        this.state = {
            appState: AppState.getState(),
        };

        window.addEventListener("bxp-appStateChanged", this._onAppStateChanged);
    }

    _onAppStateChanged = (e: CustomEvent<AppState.AppState>) => {
        this.setState({
            appState: e.detail
        });
    }

    render() {
        return (
            <nav>
                <div className="navbar-item">
                    <NavLink to="/" exact className="flex-cell">
                        <span className="nav-icon"><i className="ms-Icon ms-Icon--HardDrive"></i></span>
                        <span className="nav-text">Local builds</span>
                    </NavLink>
                </div>
                {
                    this.state.appState.mode !== "build"
                        ? undefined
                        : <div className="navbar-item">

                            <NavLink to={"/b/" + this.state.appState.sessionId} exact className="flex-cell">
                                <span className="nav-icon"><i className="ms-Icon ms-Icon--SummaryChart"></i></span>
                                <span className="nav-text">Summary</span>
                            </NavLink>
                            <NavLink to={"/b/" + this.state.appState.sessionId + "/pips"} className="flex-cell">
                                <span className="nav-icon"><i className="ms-Icon ms-Icon--GitGraph"></i></span>
                                <span className="nav-text">Pips</span>
                            </NavLink>
                        </div>
                }
                <div className="flex separator">
                    <div className="flex-cell separator-item"></div>
                </div>
                <div className="flex flex-noshrink navbar-item">
                    <NavLink className="flex-cell" to="/settings"><span className="nav-icon"><i className="ms-Icon ms-Icon--Settings"></i></span><span className="nav-text">Settings</span></NavLink>
                </div>
            </nav>
        );
    }
}
