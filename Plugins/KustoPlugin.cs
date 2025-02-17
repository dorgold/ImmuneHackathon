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

    [KernelFunction, Description("Run aribtrary KQL queries on MyTable, returns a CSV of the results or an error string")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1024:Use properties where appropriate", Justification = "Too smart")]
    public string RunQuery(
        [Description("The KQL query to run.")]
        string query)
    {
        query = query.Replace("MyTable", "materialized_view('LatestDeviceInfoProfilesView')");
        Console.WriteLine($"Running query: {query}");
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