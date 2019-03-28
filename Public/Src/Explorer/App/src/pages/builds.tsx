// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as React from 'react';
import * as DateTimeHelpers from '../models/dateTimeHelpers';
import * as LocalBuilds from "../models/localBuilds";
import * as AppState from "../models/appState"

import * as Links from "../controls/links";

import {
    MessageBar,
    MessageBarType,
    DetailsList,
    DetailsListLayoutMode,
    Selection,
    SelectionMode,
    IColumn,
} from 'office-ui-fabric-react';


interface BuildsPageData {
    localBuilds: LocalBuilds.BuildDetails[],
}


export class Builds extends React.Component<{}, BuildsPageData> {

    private _selection: Selection;

    public constructor(props: {}) {
        super(props);

        AppState.setOpen();

        this.state = {
            localBuilds: LocalBuilds.getLocalBuilds(),
        };
    }

    private _onSyncLocalBuilds = () => {
        this.setState({
            localBuilds: LocalBuilds.getLocalBuilds(),
        });
    }

    private _columns: IColumn[] = [
        {
            key: 'column1',
            name: 'SessionId',
            fieldName: 'sessionId',
            minWidth: 200,
            maxWidth: 250,
            onRender: (item: LocalBuilds.BuildDetails) => {
                return (<Links.BuildLink reference={item} sessionId={item.sessionId} />);
            }
        },
        {
            key: 'column2',
            name: 'StartTime',
            fieldName: 'startTime',
            minWidth: 100,
            maxWidth: 150,
            isResizable: true,
            onRender: (item: LocalBuilds.BuildDetails) => {
                return (<span title={DateTimeHelpers.toLongString(item.buildStartTime)}>{DateTimeHelpers.toFriendlyString(item.buildStartTime)}</span>);
            },
            isPadded: true
        },
        {
            key: 'column3',
            name: 'ConfigFile',
            fieldName: 'configFile',
            minWidth: 100,
            maxWidth: 500,
            isResizable: true,
            onRender: (item: LocalBuilds.BuildDetails) => {
                return (<span>{item.primaryConfigFile}</span>);
            },
            isPadded: true
        },
        {
            key: 'column4',
            name: 'EngineVersion',
            fieldName: 'engineVersion',
            minWidth: 100,
            maxWidth: 100,
            isResizable: true,
            onRender: (item: LocalBuilds.BuildDetails) => {
                return (<span>{item.engineVersion}</span>);
            },
        },
    ];


    render() {
        return (
            <div className="hub-page">
                <div style={{margin: "24px"}}>
                    <MessageBar
                        messageBarType={MessageBarType.warning}
                        isMultiline={true}
                        >
                        Warning: Connection- and Server Management is not fully developed yet. You might encounter some connection issues.
                    </MessageBar>
                </div>

                <div className="hub-header">
                    <h1>Select a build to open:</h1>
                </div>
                <div className="hub-body">
                    <section className="hub-box">
                        <h3>Local builds <span onClick={this._onSyncLocalBuilds}><i className="ms-Icon ms-Icon--Sync"></i></span></h3>
                        <DetailsList
                            items={this.state.localBuilds}
                            columns={this._columns}
                            compact={true}
                            selection={this._selection}
                            selectionMode={SelectionMode.none}
                            setKey="set"
                            layoutMode={DetailsListLayoutMode.justified}
                            isHeaderVisible={true}
                        />
                    </section>
                    <section className="hub-box">
                        <h3>CloudBuild builds</h3>
                        <p><i className={"ms-Icon ms-Icon--CloudNotSynced"}></i> This feature is comming soon... It is currently blocked on feature: in cloudbuild. </p>
                    </section>
                </div>
            </div>
        )
    };
}
