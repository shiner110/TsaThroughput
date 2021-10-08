using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using System.IO.Compression;
using System.IO;

using Azure.AI.FormRecognizer;
using Azure.AI.FormRecognizer.Models;
using Azure.AI.FormRecognizer.Training;

using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

using HtmlAgilityPack;
using Azure;

namespace TsaThroughputFunctions.Data
{
    public static class GetTsaThroughputFile
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        [FunctionName("GetTsaThroughputFile")]
        public static async Task Run([TimerTrigger("0 */1 * * * *")] TimerInfo myTimer, ILogger log)
        {
            log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");

            Uri latestThroughputFileUri = await GetLatestThroughputFileUriAsync(log);
            log.LogInformation($"Latest throughputfiles is {latestThroughputFileUri.AbsoluteUri}");

            var formRecognizerEndpointUri = new Uri(Environment.GetEnvironmentVariable("formRecognizerEndpointUri"));
            var formRecognizerCredential = new AzureKeyCredential(Environment.GetEnvironmentVariable("formRecognizerApiKey"));
            var client = new FormRecognizerClient(
                formRecognizerEndpointUri,
                formRecognizerCredential,
                new FormRecognizerClientOptions(FormRecognizerClientOptions.ServiceVersion.V2_0));
            RecognizeContentOptions rco = new RecognizeContentOptions 
            { 
                ContentType = FormContentType.Pdf,
                Pages = {"1-2"},
                Language = FormRecognizerLanguage.En
            };


            Response<FormPageCollection> formPagesResponse = 
                await client.StartRecognizeContentFromUri(
                    latestThroughputFileUri, rco).WaitForCompletionAsync();


            foreach (FormPage page in formPagesResponse.Value)
            {
                for (int i = 0; i < page.Tables.Count; i++)
                {
                    FormTable table = page.Tables[i];
                    int cellCursor = 0;

                    // Skip the first row of the table, as it contains titles
                    if (table.Cells[cellCursor].RowIndex == 0)
                        cellCursor += 8;
                }
            }
        }

        //
        // GetLatestThroughputFileUriAsync(log)
        // Returns the Url of the latest TsaThroughputFile
        private static async Task<Uri> GetLatestThroughputFileUriAsync(ILogger log)
        {
            string website = "https://www.tsa.gov";
            string subUrl = "/foia/readingroom/";

            var html = await GetAsyncString($"{website}{subUrl}");

            HtmlDocument doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Extract the link so we can fetch it. Make sure it is a TSA Throughput File
            // This assumes that the first file is always the latest
            var url = doc.DocumentNode
                .SelectSingleNode("//a[@class='foia-reading-link'][contains(text(),'TSA Throughput')]")
                .Attributes["href"].Value;

            return new Uri($"{website}{url}");
        }

        //
        // GetAsyncString(url)
        // Returns a string representation of the webpage for the given URL
        private static async Task<string> GetAsyncString(string url)
        {
            // Need to impersonate a browser, otherwise HTTP 403 Forbidden is returned
            // See https://stackoverflow.com/questions/15026953/httpclient-request-like-browser/15031419#15031419
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(url));
            request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml");
            request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 6.2; WOW64; rv:19.0) Gecko/20100101 Firefox/19.0");
            request.Headers.TryAddWithoutValidation("Accept-Charset", "ISO-8859-1");

            using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            using var responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var decompressedStream = new GZipStream(responseStream, CompressionMode.Decompress);
            using var streamReader = new StreamReader(decompressedStream);

            return await streamReader.ReadToEndAsync().ConfigureAwait(false);
        }
    }
}
