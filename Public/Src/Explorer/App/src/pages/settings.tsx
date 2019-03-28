// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as React from 'react';
import * as SettingsModel from "../models/settings";
import { Dropdown, IDropdownOption, ChoiceGroup, IChoiceGroupOption, TextField } from 'office-ui-fabric-react';

export class Settings extends React.Component<{}, SettingsModel.SettingsData> {

    public constructor(props: any) {
        super(props);
       
        this.state = SettingsModel.current;
    }

    private _onThemeChange = (option: IDropdownOption, index?: number) => {
        let update = {theme: option.key as SettingsModel.ThemeStyle};
        this.setState(update);
        SettingsModel.update(update);
    }

    private _onDevServerChange = (ev: React.FormEvent<HTMLInputElement>, option: IChoiceGroupOption) => {
        let update = {useDevServer: option.key == "dev"};
        this.setState(update);
        SettingsModel.update(update);
    }

    private _onDevServerNameChange = (ev: React.FormEvent<HTMLInputElement>, newValue: string) => {
        let update = {devServerName: newValue};
        this.setState(update);
        SettingsModel.update(update);
    }

    private _onDevServerPortChange = (ev: React.FormEvent<HTMLInputElement>, newValue: string) => {
        var port = parseInt(newValue);
        if (!port) {
            return;
        }

        let update = {devServerPort: port};
        this.setState(update);
        SettingsModel.update(update);
    }

    private _onGetErrorForDevServerPort = (value: string) : string | undefined => {
        var port = parseInt(value);
        if (!port) {
            return "Must be an integer"
        }
        if (port < 80 || port > 65535) {
            return "invalid port"
        }

        return undefined;
    }

    private _onMaxServersChange = (ev: React.FormEvent<HTMLInputElement>, newValue: string) => {
        var maxServers = parseInt(newValue);
        if (!maxServers) {
            return;
        }

        let update = {maxServers: maxServers};
        this.setState(update);
        SettingsModel.update(update);
    }

    private _onGetErrorForMaxServers = (value: string) : string | undefined => {
        var intValue = parseInt(value);
        if (!intValue) {
            return "Must be an integer"
        }

        return undefined;
    }

    render() {
        return (
            <div className="hub-page">
                <div className="hub-header">
                    <h1>Settings</h1>
                </div>
                <div className="hub-body">
                    <section className="hub-box">
                        <Dropdown
                            placeholder="Select a theme"
                            label="Theme:"
                            id="themeDropDown"
                            style={{marginBottom: "10px"}}
                            ariaLabel="Select a theme"
                            selectedKey={this.state.theme}
                            onChanged={this._onThemeChange}
                            options={[
                                {key: "dark", text: "Dark"},
                                {key: "light", text: "Light"},
                            ]}
                        />
                        <ChoiceGroup
                            selectedKey={this.state.useDevServer ? "dev" : "auto"}
                            style={{marginBottom: "10px"}}
                            options={[
                                {
                                    key: 'auto',
                                    text: 'Autmatically pick the right version',
                                } as IChoiceGroupOption,
                                {
                                    key: 'dev',
                                    text: 'Use dev server',
                                    onRenderField: (props, render) => {
                                        return (
                                            <div>
                                                {render!(props)}
                                                <div style={ { marginLeft: "50px" } }>
                                                    server: <TextField value={this.state.devServerName} onChange={this._onDevServerNameChange} underlined prefix="https://"  ariaLabel="DevServer hostname"/> 
                                                    port: <TextField value={this.state.devServerPort.toString()} onChange={this._onDevServerPortChange} onGetErrorMessage={this._onGetErrorForDevServerPort} underlined ariaLabel="DevServer port"/>
                                                </div>
                                            </div>);
                                    },
                                },
                            ]}
                            onChange={this._onDevServerChange}
                            label="Select which server should be launched:"
                        />
                        <TextField
                            label="Maximum number of servers active at one time:"
                            value={this.state.maxServers.toString()} 
                            onChange={this._onMaxServersChange} 
                            onGetErrorMessage={this._onGetErrorForMaxServers}
                             underlined 
                             ariaLabel="Max number of servers"/>
                    </section>
                </div>
                Temporary dev helper: All imported Icons:
                <ul>
<li><span className="nav-icon"><i className="ms-Icon ms-Icon--GitGraph"></i> ms-Icon--GitGraph</span></li>
<li><span className="nav-icon"><i className="ms-Icon ms-Icon--BarChart4"></i> ms-Icon--BarChart4</span></li>
<li><span className="nav-icon"><i className="ms-Icon ms-Icon--BubbleChart"></i> ms-Icon--BubbleChart</span></li>
<li><span className="nav-icon"><i className="ms-Icon ms-Icon--Copy"></i> ms-Icon--Copy</span></li>
<li><span className="nav-icon"><i className="ms-Icon ms-Icon--DeveloperTools"></i> ms-Icon--DeveloperTools</span></li>
<li><span className="nav-icon"><i className="ms-Icon ms-Icon--Error"></i> ms-Icon--Error</span></li>
<li><span className="nav-icon"><i className="ms-Icon ms-Icon--FolderHorizontal"></i> ms-Icon--FolderHorizontal</span></li>
<li><span className="nav-icon"><i className="ms-Icon ms-Icon--GitGraph"></i> ms-Icon--GitGraph</span></li>
<li><span className="nav-icon"><i className="ms-Icon ms-Icon--HardDrive"></i> ms-Icon--HardDrive</span></li>
<li><span className="nav-icon"><i className="ms-Icon ms-Icon--Search"></i> ms-Icon--Search</span></li>
<li><span className="nav-icon"><i className="ms-Icon ms-Icon--Settings"></i> ms-Icon--Settings</span></li>
<li><span className="nav-icon"><i className="ms-Icon ms-Icon--SetTopStack"></i> ms-Icon--SetTopStack</span></li>
<li><span className="nav-icon"><i className="ms-Icon ms-Icon--Tag"></i> ms-Icon--Tag</span></li>
<li><span className="nav-icon"><i className="ms-Icon ms-Icon--TextDocument"></i> ms-Icon--TextDocument</span></li>
<li><span className="nav-icon"><i className="ms-Icon ms-Icon--OpenFolderHorizontal"></i> ms-Icon--OpenFolderHorizontal</span></li>
<li><span className="nav-icon"><i className="ms-Icon ms-Icon--FolderSearch"></i> ms-Icon--FolderSearch</span></li>
<li><span className="nav-icon"><i className="ms-Icon ms-Icon--PublicFolder"></i> ms-Icon--PublicFolder</span></li>
<li><span className="nav-icon"><i className="ms-Icon ms-Icon--FolderQuery"></i> ms-Icon--FolderQuery</span></li>
<li><span className="nav-icon"><i className="ms-Icon ms-Icon--FolderList"></i> ms-Icon--FolderList</span></li>
<li><span className="nav-icon"><i className="ms-Icon ms-Icon--FolderListMirrored"></i> ms-Icon--FolderListMirrored</span></li>
<li><span className="nav-icon"><i className="ms-Icon ms-Icon--VisualsFolder"></i> ms-Icon--VisualsFolder</span></li>
<li><span className="nav-icon"><i className="ms-Icon ms-Icon--Copper"></i> ms-Icon--Copper</span></li>
<li><span className="nav-icon"><i className="ms-Icon ms-Icon--DocumentApproval"></i> ms-Icon--DocumentApproval</span></li>
<li><span className="nav-icon"><i className="ms-Icon ms-Icon--DownloadDocument"></i> ms-Icon--DownloadDocument</span></li>
<li><span className="nav-icon"><i className="ms-Icon ms-Icon--OpenFile"></i> ms-Icon--OpenFile</span></li>
<li><span className="nav-icon"><i className="ms-Icon ms-Icon--OpenDocument"></i> ms-Icon--OpenDocument</span></li>
<li><span className="nav-icon"><i className="ms-Icon ms-Icon--DocumentReply"></i> ms-Icon--DocumentReply</span></li>
<li><span className="nav-icon"><i className="ms-Icon ms-Icon--DocumentSearch"></i> ms-Icon--DocumentSearch</span></li>
<li><span className="nav-icon"><i className="ms-Icon ms-Icon--FileCode"></i> ms-Icon--FileCode</span></li>
<li><span className="nav-icon"><i className="ms-Icon ms-Icon--Cloud"></i> ms-Icon--Cloud</span></li>
<li><span className="nav-icon"><i className="ms-Icon ms-Icon--CloudNotSynced"></i> ms-Icon--CloudNotSynced</span></li>
<li><span className="nav-icon"><i className="ms-Icon ms-Icon--SummaryChart"></i> ms-Icon--SummaryChart</span></li>
<li><span className="nav-icon"><i className="ms-Icon ms-Icon--Sync"></i> ms-Icon--Sync</span></li>
<li><span className="nav-icon"><i className="ms-Icon ms-Icon--GoToMessage"></i> ms-Icon--GoToMessage</span></li>
<li><span className="nav-icon"><i className="ms-Icon ms-Icon--Warning"></i> ms-Icon--Warning</span></li>
<li><span className="nav-icon"><i className="ms-Icon ms-Icon--Error"></i> ms-Icon--Error</span></li>
<li><span className="nav-icon"><i className="ms-Icon ms-Icon--Info"></i> ms-Icon--Info</span></li>
                    <li></li>
                </ul>
            </div>
        );
    }
}
