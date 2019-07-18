// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as React from 'react';

import * as References from "../models/references";
import { PipTable } from "../controls/pipTable"
import { AsyncBuildPage, AsyncRouteProps, BuildRouteParams } from "../pages/asyncBuildPage";

import {
    Dropdown,
    DropdownMenuItemType,
    IDropdownOption,
    TextField,
} from 'office-ui-fabric-react';


interface PipResults {
    items: References.PipRefWithDetails[],
}

interface LocalState {
    filterSemiStableHash: string | undefined,
    filterKind: string | undefined,
    filterDescription: string | undefined,
}

export class Pips extends AsyncBuildPage<BuildRouteParams, PipResults, LocalState> {
    public constructor(props: AsyncRouteProps<BuildRouteParams>) {
        super(props);

        this.state = Object.assign(this.state, {
            data: {
                filterKind: "process"
            }
        });
    }

    getLocalDefault(): LocalState {
        return {
            filterSemiStableHash: undefined,
            filterKind: "process",
            filterDescription: undefined,
        }
    }

    getRequestUrl(props: BuildRouteParams): string {
        let local = this.state.local;
        let url = `/b/${props.sessionId}/pips`;

        if (local.filterSemiStableHash) {
            url = this.addQueryParam(url, "semiStableHash", local.filterSemiStableHash);
        }

        if (local.filterKind) {
            url = this.addQueryParam(url, "kind", local.filterKind);
        }

        if (local.filterDescription) {
            url = this.addQueryParam(url, "description", local.filterDescription);
        }

        return url;
    }

    private _onFilterKindChanged = (option: IDropdownOption, index?: number) => {
        let local = this.state.local;
        if (local.filterKind === option.key) {
            return;
        }

        this.setState({
            local: Object.assign(local, {
                filterKind: option.key
            })
        });

        // Requery
        this.refetch();
    }


    private _onSemiStableHashChanged = (ev: React.FormEvent<HTMLInputElement>, newValue: string) => {
        let local = this.state.local;
        if (local.filterSemiStableHash === newValue) {
            return;
        }

        this.setState({
            local: Object.assign(local, {
                filterSemiStableHash: newValue
            })
        });
    }

    private _onDescriptionChanged = (ev: React.FormEvent<HTMLInputElement>, newValue: string) => {
        let local = this.state.local;
        if (local.filterDescription === newValue) {
            return;
        }
        this.setState({
            local: Object.assign(local, {
                filterDescription: newValue
            })
        });
    }

    private _onTextFieldBlur = () => {
        // Requery
        this.refetch();
    }

    private _onTextFieldKeyPress = (ev: React.KeyboardEvent<Element>) => {
        if (ev.key === "Enter") {
            this.refetch();
        }
    }

    renderData(data: PipResults) {
        return (
            <div className="hub-page">
                <div className="hub-header">
                    <h1>Pips</h1>
                </div>
                <div className="hub-body">
                    <div className="filter-panel" role="region" aria-label="Pip filter">
                        <div className="pipFilter-semiStableHash">
                            <TextField
                                placeholder="SemiStableHash"
                                value={this.state.local.filterSemiStableHash}
                                onChange={this._onSemiStableHashChanged}
                                onKeyPress={this._onTextFieldKeyPress}
                                onBlur={this._onTextFieldBlur}
                                ariaLabel="Please enter the semi stable has to filter by here"
                            />
                        </div>
                        <div className="pipFilter-pipKind">
                            <Dropdown
                                placeHolder="Pip Kind"
                                selectedKey={this.state.local.filterKind}
                                onChanged={this._onFilterKindChanged}
                                options={[
                                    { key: 'Header1', text: 'Primary kinds', itemType: DropdownMenuItemType.Header },
                                    { key: 'process', text: 'Process' },
                                    { key: 'writeFile', text: 'WriteFile' },
                                    { key: 'Header1', text: 'Lightweight kinds', itemType: DropdownMenuItemType.Header },
                                    { key: 'copyFile', text: 'CopyFile' },
                                    { key: 'sealDirectory', text: 'SealDirectory' },
                                    { key: 'ipc', text: 'Ipc' },
                                    { key: 'Header2', text: 'Meta Pips', itemType: DropdownMenuItemType.Header },
                                    { key: 'value', text: 'Value' },
                                    { key: 'specFile', text: 'SpecFile' },
                                    { key: 'module', text: 'Module' },
                                ]}
                            />
                        </div>
                        <div className="pipFilter-description">
                            <TextField
                                placeholder="Description"
                                ariaLabel="Please enter the semi stable has to filter by here"
                                value={this.state.local.filterDescription}
                                onChange={this._onDescriptionChanged}
                                onKeyPress={this._onTextFieldKeyPress}
                                onBlur={this._onTextFieldBlur}
                            />
                        </div>
                    </div>
                    <PipTable
                        items={data.items}
                        sessionId={this.getSessionId()}
                    />
                </div>
            </div>
        );
    }
}
