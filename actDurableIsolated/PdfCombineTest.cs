using actDurableIsolated;
using Aspose.Pdf.Facades;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using System.Net;

namespace durableFuncLogiApp
{
    // This class initiates the PDF processing by triggering the orchestrator function.
    

    // This class is a utility for testing, simulating the process of uploading and merging PDFs.
    public class TestFunction
    {
        private static HttpClient httpClient = new HttpClient();

        [Function("TestPdfUploadAndProcess")]
        public async Task<HttpResponseMessage> RunAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestData req, FunctionContext context)
        {
            var _logger = context.GetLogger<TestFunction>();
            string folderName = req.Query["foldername"] ?? "DefaultFolderName";  // If no folder name is provided, "DefaultFolderName" will be used.
            await UploadTestPdfs(folderName, _logger);
            return new HttpResponseMessage(HttpStatusCode.OK);
            //return await TriggerPdfProcessing(folderName, _logger);
        }


        private static async Task UploadTestPdfs(string folderName, ILogger log)
        {
            int numberOfPdfs = 100;

            for (int i = 1; i <= numberOfPdfs; i++)
            {
                var pdfDocument = new Aspose.Pdf.Document();
                var page = pdfDocument.Pages.Add();
                page.Paragraphs.Add(new Aspose.Pdf.Text.TextFragment($"Test PDF {i} in folder {folderName}") { Position = new Aspose.Pdf.Text.Position(100, 600) });

                var pdfStream = new MemoryStream();
                pdfDocument.Save(pdfStream);

                var blobClient = new BlobClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "pdf-container", $"{folderName}/testpdf_{i.ToString().PadLeft(3, '0')}.pdf");
                pdfStream.Position = 0; // Reset stream position
                await blobClient.UploadAsync(pdfStream, true);
            }

            log.LogInformation($"Uploaded {numberOfPdfs} PDFs to folder {folderName}");
        }

        private static async Task<HttpResponseMessage> TriggerPdfProcessing(string folderName, ILogger log)
        {
            string processingFunctionUrl = $"http://{Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME")}/api/StartPdfProcessing?foldername={folderName}";
            var response = await httpClient.GetAsync(processingFunctionUrl);
            if (!response.IsSuccessStatusCode)
            {
                return new HttpResponseMessage { StatusCode = response.StatusCode, Content = new StringContent($"Failed to start processing: {response.ReasonPhrase}") };
            }
            else
            {
                log.LogInformation($"Started processing for folder {folderName}");
                return response;
            }
        }
    }
}
