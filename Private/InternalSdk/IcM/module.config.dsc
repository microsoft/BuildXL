// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

module({
    name: "Microsoft.AzureAd.Icm.Types.amd64", 
    projects: [f`Microsoft.AzureAd.Icm.Types.amd64.dsc`]
});

module({
    name: "Microsoft.AzureAd.Icm.WebService.Client.amd64", 
    projects: [f`Microsoft.AzureAd.Icm.WebService.Client.amd64.dsc`]
});

module({
    name: "System.Private.ServiceModel", 
    projects: [f`System.Private.ServiceModel.dsc`]
});

module({
    name: "System.ServiceModel.Http", 
    projects: [f`System.ServiceModel.Http.dsc`]
});

module({
    name: "System.ServiceModel.Primitives", 
    projects: [f`System.ServiceModel.Primitives.dsc`]
});
