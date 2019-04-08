// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as React from 'react';

import * as References from "../models/references";
import * as Links from "../controls/links";

import {
    DetailsList,
    DetailsListLayoutMode,
    Selection,
    SelectionMode,
    IColumn
} from 'office-ui-fabric-react';

interface Props {
    items: References.PipRefWithDetails[]
    sessionId: string,
}

export class PipTable extends React.Component<Props, {}> {

    private _selection: Selection;

    private _columns: IColumn[] = [
        {
            key: 'column0',
            name: 'PipId',
            fieldName: 'semiStableHash',
            minWidth: 100,
            maxWidth: 150,
            onRender: (item: References.PipRefWithDetails) => {
                return (<Links.PipLink renderIcon={false} reference={item} sessionId={this.props.sessionId} />);
            }
        },
        {
            key: 'column1',
            name: 'Kind',
            fieldName: 'kind',
            minWidth: 50,
            maxWidth: 100,
            onRender: (item: References.PipRefWithDetails) => {
                return item.kind;
            }
        },
        {
            key: 'column2',
            name: 'Module',
            fieldName: 'module',
            minWidth: 100,
            maxWidth: 200,
            isResizable: true,
            onRender: (item: References.PipRefWithDetails) => {
                return (<Links.ModuleLink renderIcon={false} reference={item.module} sessionId={this.props.sessionId} />);
            },
            isPadded: true
        },
        {
            key: 'column3',
            name: 'Project',
            fieldName: 'specFile',
            minWidth: 100,
            maxWidth: 300,
            isResizable: true,
            onRender: (item: References.PipRefWithDetails) => {
                return (<Links.SpecFileLink renderIcon={false} reference={item.specFile} sessionId={this.props.sessionId} />);
            },
            isPadded: true
        },
        {
            key: 'column4',
            name: 'Value',
            fieldName: 'value',
            minWidth: 100,
            maxWidth: 300,
            isResizable: true,
            onRender: (item: References.PipRefWithDetails) => {
                return (<Links.ValueLink renderIcon={false} reference={item.value} sessionId={this.props.sessionId} />);
            },
        },
        {
            key: 'column5',
            name: 'Tool',
            fieldName: 'tool',
            minWidth: 75,
            maxWidth: 150,
            isResizable: true,
            isCollapsable: true,
            onRender: (item: References.PipRefWithDetails) => {
                return (item.tool ? <Links.ToolLink renderIcon={false} reference={item.tool} sessionId={this.props.sessionId} /> : undefined);
            }
        },
        {
            key: 'column6',
            name: 'Qualifier',
            fieldName: 'qualifier',
            minWidth: 100,
            maxWidth: 300,
            isResizable: true,
            isCollapsable: true,
            onRender: (item: References.PipRefWithDetails) => {
                return (<Links.QualifierLink renderIcon={false} reference={item.qualifier} sessionId={this.props.sessionId} />);
            }
        }
    ];

    render() {
        this._selection = new Selection({ canSelectItem: _ => false });

        if (this.props.items === undefined || this.props.items.length === 0)
        {
            return <span>No pips</span>
        }

        return (<DetailsList
            items={this.props.items}
            columns={this._columns}
            compact={true}
            selection={this._selection}
            selectionMode={SelectionMode.none}
            setKey="set"
            layoutMode={DetailsListLayoutMode.justified}
            isHeaderVisible={true}
        />);
    }
}
