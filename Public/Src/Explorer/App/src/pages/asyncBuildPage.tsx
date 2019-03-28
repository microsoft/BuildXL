// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as React from 'react';
import { match } from 'react-router-dom'
import * as BuildServerManager from "../models/buildServerManager"
import * as AppState from "../models/appState"

import {
    Spinner, 
    SpinnerSize,
    MessageBar,
    MessageBarType,
} from 'office-ui-fabric-react';

export interface AsyncState<TRequest, TLocal> {
    loading: boolean,
    data?: TRequest,
    local: TLocal,
    errorDetails?: string
}

export interface AsyncRouteProps<TProps> {
    match: match<TProps>;
}

export interface BuildRouteParams {
    sessionId: string,
}

export abstract class AsyncBuildPage<TProps extends BuildRouteParams, TRequestState, TLocalState = {}> extends React.Component<AsyncRouteProps<TProps>, AsyncState<TRequestState, TLocalState>> {

    public constructor(props: AsyncRouteProps<TProps>) {
        super(props);

        AppState.setBuild(this.getSessionId());

        this.state = {
            loading: true,
            local: this.getLocalDefault(),
        };

        this.fetch();
    }

    getLocalDefault() : TLocalState {
        return {} as TLocalState;
    }

    refetch() {
        this.setState({
            loading: true,
            errorDetails: undefined,
        });
        this.fetch();
    }

    fetch() {
        this.send(this.getRouteParams())
            .then(data => {
                this.setState({
                    loading: false,
                    data: data,
                    errorDetails: undefined
                });
            })
            .catch(err => {
                this.setState({
                    loading: false,
                    data: undefined,
                    errorDetails: `Failed to load page: ${err}`,
                });
            });
    }

    componentDidUpdate(prevProps: AsyncRouteProps<TProps>) {
        var prevUrl = this.getRequestUrl(prevProps.match.params);
        var updatedUrl = this.getRequestUrl(this.props.match.params);

        if (prevUrl != updatedUrl) {
            this.setState({
                loading: true,
                data: undefined,
                errorDetails: undefined,
            });
            this.fetch();
        }
      }

    addQueryParam(url: string, key: string, value: string) : string {
        // Add separtor
        url += url.indexOf("?") >= 0 ? "&" : "?";
        url += encodeURIComponent(key);
        url += "=";
        url += encodeURIComponent(value);

        return url;
    }


    async send(routeParams: TProps) : Promise<TRequestState> {
        var connection = await BuildServerManager.getConnection(routeParams.sessionId);
        var requestUrl = this.getRequestUrl(routeParams);
        
        return await connection.sendRequest<TRequestState>(requestUrl);
    }

    getRouteParams() : TProps {
        return this.props.match.params;
    }

    getSessionId() : string {
        return this.props.match.params.sessionId;
    }

    render() {
        if (this.state.loading) {
            return <div style={{margin: "24px"}}><Spinner size={SpinnerSize.large} label="Loading data..." ariaLive="assertive" /></div>
        }
        else if (this.state.data) {
            return this.renderData(this.state.data);
        }
        else {
            return <div style={{margin: "24px"}}><MessageBar messageBarType={MessageBarType.error}>Error loading page: {this.state.errorDetails}</MessageBar></div>
        }
    }

    abstract getRequestUrl(props: TProps): string;
    abstract renderData(state: TRequestState): React.ReactNode;

}
