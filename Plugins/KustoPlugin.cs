// Copyright (c) Microsoft. All rights reserved.

using System.ComponentModel;
using Kusto.Cloud.Platform.Data;
using Kusto.Data;
using Kusto.Data.Common;
using Kusto.Data.Net.Client;
using Microsoft.SemanticKernel;

namespace ImmuneGettingStartedSemanticKernel.Plugins;

public class KustoPlugin
{
    private string letMyTable = """
                                let tenant = "1c48f0fc-5645-4f49-b0a3-b0833a58680a";
                                let MyTable = cluster('https://eusorncmbesekustodev1.eastus.kusto.windows.net').database('CloudMapV2').GetNodesV2AllTenantsAllScopes
                                | where TenantId == tenant
                                | where Label == "device"
                                | where EntityIds has_cs 'DeviceInventoryId'
                                | mv-expand EntityId=EntityIds
                                | where EntityId.type == 'DeviceInventoryId'
                                | extend DeviceId = tostring(EntityId.id)
                                | extend DeviceName = Name
                                | extend DeviceCategory = iff(isempty(tostring(Properties.deviceCategory)), "Unknown", tostring(Properties.deviceCategory))
                                | extend DeviceType = iff(isempty(tostring(Properties.deviceType)), "Unknown", tostring(Properties.deviceType))
                                | extend ExposureScore = iff(isempty(tostring(Properties.exposureScore)), "None", tostring(Properties.exposureScore))
                                | extend OsDistribution = iff(isempty(tostring(Properties.osDistribution)), "Unknown", tostring(Properties.osDistribution))
                                | extend OsPlatformFriendlyName = iff(isempty(tostring(Properties.osPlatformFriendlyName)), "Unknown", tostring(Properties.osPlatformFriendlyName))
                                | extend OsBuildRevision = iff(isempty(tostring(Properties.osBuildRevision)), "Unknown", tostring(Properties.osBuildRevision))
                                | extend OsBuildRevisionBins = case(
                                OsBuildRevision == "Unknown", "Unknown",
                                toint(OsBuildRevision) >= 1 and toint(OsBuildRevision) <= 5000, "1-5000",
                                toint(OsBuildRevision) > 5000 and toint(OsBuildRevision) <= 10000, "5000-10000",
                                toint(OsBuildRevision) > 10000, ">10000",
                                "Unknown" 
                                )
                                | extend HasTpmData = isnotempty(Properties.tpmData)
                                | extend OsVersion = iff(isempty(tostring(Properties.osVersion)), "Unknown", tostring(Properties.osVersion))
                                | extend HasrdpStatus = isnotempty(Properties.rdpStatus)
                                | extend HasremoteServices = isnotempty(Properties.remoteServicesInfo)
                                | extend OsArchitecture = iff(isempty(tostring(Properties.osArchitecture)), "Unknown", tostring(Properties.osArchitecture))
                                | extend OsBuild = iff(isempty(tostring(Properties.osBuild)), "Unknown", tostring(Properties.osBuild))
                                | extend OsBuildBins = case(
                                OsBuild == "Unknown", "Unknown",
                                toint(OsBuild) >= 1 and toint(OsBuild) <= 5000, "1-5000",
                                toint(OsBuild) > 5000 and toint(OsBuild) <= 10000, "5000-10000",
                                toint(OsBuild) > 10000, ">10000",
                                "Unknown" 
                                )
                                | extend DeviceDynamicTags = tostring(Properties.deviceDynamicTags)
                                | where OsDistribution == 'Windows'
                                | where DeviceCategory == 'Endpoint'
                                | where DeviceType in ('Workstation', 'Server')
                                | where OsPlatformFriendlyName startswith 'Windows'
                                | project TenantId, Id, DeviceId, DeviceName, DeviceType, ExposureScore, OsPlatformFriendlyName, OsBuildRevisionBins, HasTpmData, OsVersion, HasrdpStatus, HasremoteServices, OsArchitecture, OsBuildBins, DeviceDynamicTags;
                                """;

    public KustoPlugin()
    {
        var clusterUri = "https://immunequerystg.eastus2.kusto.windows.net/";
        var kcsb = new KustoConnectionStringBuilder(clusterUri)
            .WithAadUserPromptAuthentication();

        KustoClient = KustoClientFactory.CreateCslQueryProvider(kcsb);
    }

    private ICslQueryProvider KustoClient { get; set; }

    // [KernelFunction, Description("Get table MyTable schema")]
    // [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1024:Use properties where appropriate", Justification = "Too smart")]
    // public string GetTableSchema()
    // {
    //     var response = KustoClient.ExecuteQuery("PhoenixData", @"materialized_view('LatestDeviceInfoProfilesView') | getschema", null);
    //     using TextWriter stringWriter = new StringWriter();
    //     response.WriteAsCsv(true, stringWriter);
    //     return stringWriter.ToString()!;
    // }

    [KernelFunction("Query"), Description("Run aribtrary KQL queries on MyTable, returns a CSV of the results or an error string")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1024:Use properties where appropriate", Justification = "Too smart")]
    public string RunQuery(
        [Description("The KQL query to run.")]
        string query)
    {
        // query = query.Replace("MyTable", "materialized_view('LatestDeviceInfoProfilesView') | where OrgId == 'd650c28a-47bd-491c-8426-42847b4c865b'");
        Console.WriteLine($"Running query: {query}");
        query = letMyTable + query + " | take 5000";
        try
        {
            var response = KustoClient.ExecuteQuery("PhoenixData", query, null);
            using TextWriter stringWriter = new StringWriter();
            response.WriteAsCsv(true, stringWriter);
            return stringWriter.ToString()!;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    [KernelFunction("Score"), Description("Score gets a comma separated list of unique DeviceIds and return a score for the uniquness of the set. The score is a value between 0 and 1")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1024:Use properties where appropriate", Justification = "Too smart")]
    public string Score(
        [Description("Comma sepparated list of unique DeviceIds.")]
        List<string> DeviceIds)
    {
        var query = $"""
                     let tenant = "1c48f0fc-5645-4f49-b0a3-b0833a58680a";
                     let EnvDevices = cluster('https://eusorncmbesekustodev1.eastus.kusto.windows.net').database('CloudMapV2').GetNodesV2AllTenantsAllScopes
                     | where TenantId == tenant
                     | where Label == "device"
                     | where EntityIds has_cs 'DeviceInventoryId'
                     | mv-expand EntityId=EntityIds
                     | where EntityId.type == 'DeviceInventoryId'
                     | extend DeviceId = tostring(EntityId.id)
                     | extend DeviceName = Name
                     | extend DeviceCategory = iff(isempty(tostring(Properties.deviceCategory)), "Unknown", tostring(Properties.deviceCategory))
                     | extend DeviceType = iff(isempty(tostring(Properties.deviceType)), "Unknown", tostring(Properties.deviceType))
                     | extend ExposureScore = iff(isempty(tostring(Properties.exposureScore)), "None", tostring(Properties.exposureScore))
                     | extend OsDistribution = iff(isempty(tostring(Properties.osDistribution)), "Unknown", tostring(Properties.osDistribution))
                     | extend OsPlatformFriendlyName = iff(isempty(tostring(Properties.osPlatformFriendlyName)), "Unknown", tostring(Properties.osPlatformFriendlyName))
                     | extend OsBuildRevision = iff(isempty(tostring(Properties.osBuildRevision)), "Unknown", tostring(Properties.osBuildRevision))
                     | extend OsBuildRevisionBins = case(
                        OsBuildRevision == "Unknown", "Unknown",
                        toint(OsBuildRevision) >= 1 and toint(OsBuildRevision) <= 5000, "1-5000",
                        toint(OsBuildRevision) > 5000 and toint(OsBuildRevision) <= 10000, "5000-10000",
                        toint(OsBuildRevision) > 10000, ">10000",
                        "Unknown" 
                     )
                     | extend HasTpmData = isnotempty(Properties.tpmData)
                     | extend OsVersion = iff(isempty(tostring(Properties.osVersion)), "Unknown", tostring(Properties.osVersion))
                     | extend HasrdpStatus = isnotempty(Properties.rdpStatus)
                     | extend HasremoteServices = isnotempty(Properties.remoteServicesInfo)
                     | extend OsArchitecture = iff(isempty(tostring(Properties.osArchitecture)), "Unknown", tostring(Properties.osArchitecture))
                     | extend OsBuild = iff(isempty(tostring(Properties.osBuild)), "Unknown", tostring(Properties.osBuild))
                     | extend OsBuildBins = case(
                        OsBuild == "Unknown", "Unknown",
                        toint(OsBuild) >= 1 and toint(OsBuild) <= 5000, "1-5000",
                        toint(OsBuild) > 5000 and toint(OsBuild) <= 10000, "5000-10000",
                        toint(OsBuild) > 10000, ">10000",
                        "Unknown" 
                     )
                     | extend DeviceDynamicTags = tostring(Properties.deviceDynamicTags)
                     | where OsDistribution == 'Windows'
                     | where DeviceCategory == 'Endpoint'
                     | where DeviceType in ('Workstation', 'Server')
                     | where OsPlatformFriendlyName startswith 'Windows'
                     | distinct TenantId, Id, DeviceId, DeviceName, DeviceType, ExposureScore, OsPlatformFriendlyName, OsBuildRevisionBins, HasTpmData, OsVersion, HasrdpStatus, HasremoteServices, OsArchitecture, OsBuildBins, DeviceDynamicTags;
                     let SampleDeviceGroups = EnvDevices;
                     let devicesCount = toscalar(EnvDevices | count);
                     // Calculate the representative groups and their weighted counts
                     let RepresentativeGroups = EnvDevices
                     | summarize RepresentativeGroupsCount = count() by DeviceType, ExposureScore, OsPlatformFriendlyName, OsBuildRevisionBins, HasTpmData, OsVersion, HasrdpStatus, HasremoteServices, OsArchitecture, OsBuildBins
                     | extend WeightedCount = RepresentativeGroupsCount * 1.0 / devicesCount;
                     // Calculate the accumulated sum for the representativeScore groups of chosen machines
                     let RepresentativeScore = SampleDeviceGroups
                     | distinct DeviceType, ExposureScore, OsPlatformFriendlyName, OsBuildRevisionBins, HasTpmData, OsVersion, HasrdpStatus, HasremoteServices, OsArchitecture, OsBuildBins
                     | join kind=inner (RepresentativeGroups) on DeviceType, ExposureScore, OsPlatformFriendlyName, OsBuildRevisionBins, HasTpmData, OsVersion, HasrdpStatus, HasremoteServices, OsArchitecture, OsBuildBins
                     | summarize RepresentativeScore = round(sum(WeightedCount), 2);
                     RepresentativeScore
                     """;
        Console.WriteLine($"Running Score");
        try
        {
            var response = KustoClient.ExecuteQuery("PhoenixData", query, null);
            using TextWriter stringWriter = new StringWriter();
            response.WriteAsCsv(true, stringWriter);
            return stringWriter.ToString()!;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }
}