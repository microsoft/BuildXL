/*---------------------------------------------------------
 * Copyright (C) Microsoft Corporation. All rights reserved.
 *--------------------------------------------------------*/
'use strict';
Object.defineProperty(exports, "__esModule", { value: true });
var request = require('request');
var URL = require('url-parse');
function getContents(url, token, headers, callback) {
    request.get(toRequestOptions(url, token, headers), function (error, response, body) {
        if (!error && response && response.statusCode >= 400) {
            error = new Error('Request returned status code: ' + response.statusCode + '\nDetails: ' + response.body);
        }
        callback(error, body);
    });
}
exports.getContents = getContents;
function toRequestOptions(url, token, headers) {
    headers = headers || {
        'user-agent': 'nodejs'
    };
    if (token) {
        headers['Authorization'] = 'token ' + token;
    }
    var parsedUrl = new URL(url);
    var options = {
        url: url,
        headers: headers
    };
    // We need to test the absence of true here because there is an npm bug that will not set boolean
    // env variables if they are set to false.
    if (process.env.npm_config_strict_ssl !== 'true') {
        options.strictSSL = false;
    }
    if (process.env.npm_config_proxy && parsedUrl.protocol === 'http:') {
        options.proxy = process.env.npm_config_proxy;
    }
    else if (process.env.npm_config_https_proxy && parsedUrl.protocol === 'https:') {
        options.proxy = process.env.npm_config_https_proxy;
    }
    return options;
}
exports.toRequestOptions = toRequestOptions;
