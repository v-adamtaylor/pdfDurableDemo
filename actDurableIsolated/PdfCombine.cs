using actDurableIsolated;
using Aspose.Pdf.Annotations;
using Aspose.Pdf;
using Aspose.Pdf.Facades;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Abstractions;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Net;
using Aspose.Pdf.Text;
using DurableTask.Core;
using System.Collections.Generic;

namespace durableFuncLogiApp
{
    public class PdfBlob
    {
        public string Name { get; set; }
        public Uri BlobUri { get; set; }
        public string TempFilePath { get; set; }
    }

    public class UploadPdfInput
    {
        public string FolderName { get; set; }
        public string MergedFilePath { get; set; }
    }


    public class pdfMerger
    {
        private readonly ILogger _logger;

        // Constructor to initialize logger
        public pdfMerger(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<pdfMerger>();
        }

        public static string GetPdfTempFileName()
        {
            return $"{Path.GetTempPath()}{Path.GetFileNameWithoutExtension(Path.GetRandomFileName())}.pdf";
        }

        private string getConnectionString { get { return Environment.GetEnvironmentVariable("AzureWebJobsStorage"); } }


        // Entry point for the PDF processing. Initiates the PDF merging orchestration.
        [Function("StartPdfProcessing")]
        public async Task<HttpResponseData> HttpStart(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestData req,
            [DurableClient] DurableTaskClient client,
            FunctionContext executionContext)
        {
            string foldername = req.Query["foldername"] ?? "DefaultFolderName";
            string instanceId = await client.ScheduleNewOrchestrationInstanceAsync("PdfMerger_Orchestrator", foldername);

            _logger.LogInformation($"Started processing blobs in folder: {foldername}. Orchestration ID: {instanceId}");

            // Return the check status response to the client
            return client.CreateCheckStatusResponse(req, instanceId);
        }


        // Orchestrator function that coordinates fetching, merging, and finalizing PDFs.
        [Function("PdfMerger_Orchestrator")]
        public async Task<Uri> RunOrchestrator([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var folderName = context.GetInput<string>();

            List<PdfBlob> blobNames = await context.CallActivityAsync<List<PdfBlob>>("FetchPDFs", folderName);

            var tasks = blobNames.Select(x => context.CallActivityAsync<PdfBlob>("DownloadPdf", new PdfBlob { Name = x.Name })).ToList();
            PdfBlob[] blobArray = await Task.WhenAll(tasks);
            List<PdfBlob> myblobs = blobArray.ToList();
            var pdfBlobOutputsAndTOC = await context.CallActivityAsync<List<string>>("GenerateTOC", myblobs.Select(blob => blob.TempFilePath).ToList());
            var pdfMergedFilePath = await context.CallActivityAsync<string>("MergePDFs", pdfBlobOutputsAndTOC);
            var pdfUploadUri = await context.CallActivityAsync<Uri>("UploadPDFs", new UploadPdfInput { 
                FolderName = folderName, MergedFilePath = pdfMergedFilePath });

            pdfBlobOutputsAndTOC.ForEach(x => File.Delete(x));
            File.Delete(pdfMergedFilePath);

            return pdfUploadUri;
        }

        // Function to fetch PDF URIs from a specified folder in Azure Blob storage.
        [Function("FetchPDFs")]
        public async Task<List<PdfBlob>> FetchPDFs([ActivityTrigger] string folderName)
        {
            try
            {
                var blobContainer = new BlobContainerClient(getConnectionString, "pdf-container");
                var blobIterator = blobContainer.GetBlobsAsync(prefix: folderName).GetAsyncEnumerator();

                List<PdfBlob> pdfBlobs = new List<PdfBlob>();

                await foreach (BlobItem blobItem in blobContainer.GetBlobsAsync(prefix: folderName))
                {
                    if (blobItem.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    {                       
                        pdfBlobs.Add( new PdfBlob { Name = blobItem.Name });
                    }
                }

                _logger.LogInformation($"Fetched {pdfBlobs.Count} PDF Tasks from Folder: {folderName}.");

                return pdfBlobs;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching PDF Tasks from folder: {folderName}.");
                throw;
            }
        }

        [Function("DownloadPDF")]
        public async Task<PdfBlob> DownloadPDF([ActivityTrigger] PdfBlob pdfBlobInput)
        {
            var blobContainer = new BlobContainerClient(getConnectionString, "pdf-container");

            pdfBlobInput.TempFilePath = GetPdfTempFileName();
            await blobContainer.GetBlobClient(pdfBlobInput.Name).DownloadToAsync(pdfBlobInput.TempFilePath);
            _logger.LogInformation($"Downloaded PDF - Name: {pdfBlobInput.Name}, Path: {pdfBlobInput.TempFilePath}");

            return pdfBlobInput;
        }

        // Function to generate a Table of Contents (TOC) for merged PDFs.
        [Function("GenerateTOC")]
        public async Task<List<string>> GenerateTOC([ActivityTrigger] List<string> tempFilePaths)
        {
            var tocDocument = new Aspose.Pdf.Document();
            var tocPage = tocDocument.Pages.Add();

            for (int i = 0; i < 3; i++)
            {
                var currentPage = (new Aspose.Pdf.Document(tempFilePaths[i])).Pages;
                tocDocument.Pages.Add(currentPage);

                // Create link
                var textFragment = new Aspose.Pdf.Text.TextFragment
                {
                    Text = $"Document {i}: {Path.GetFileName(tempFilePaths[i])} Test PDF:{i + 1} Page:{i + 2}",
                    Position = new Aspose.Pdf.Text.Position(100, 700 - (i * 20)),
                    Hyperlink = new Aspose.Pdf.LocalHyperlink() { TargetPageNumber = i + 2 }
                };

                tocPage.Paragraphs.Add(textFragment);
            }

            var tempFilePath = GetPdfTempFileName();
            tocDocument.Save(tempFilePath);
            tempFilePaths.Insert(0, tempFilePath);

            _logger.LogInformation($"Generated and Saved Table of Contents for {tempFilePaths.Count} Documents.");

            return tempFilePaths;
        }

        // Function to finalize the merge process and include the TOC.
        [Function("MergePDFs")]
        public async Task<string> MergePDFs([ActivityTrigger] List<string> pathsToMerge)
        {
            var mergeTempFilePath = GetPdfTempFileName();

            var editor = new PdfFileEditor();
            editor.Concatenate(pathsToMerge.ToArray(), mergeTempFilePath);
            _logger.LogInformation($"Merged PDF with TOC created at {mergeTempFilePath}");

            return mergeTempFilePath;
        }

        // Uploads the merged PDF to the blob container and returns its URI.
        [Function("UploadPDFs")]
        public async Task<Uri> UploadPDFs([ActivityTrigger] UploadPdfInput input)
        {
            var blobContainer = new BlobContainerClient(getConnectionString, "pdf-container");
            var blobClient = blobContainer.GetBlobClient($"_{input.FolderName}_{DateTime.Now.ToString("u")}.pdf");
            using var uploadFile = File.OpenRead(input.MergedFilePath);

            await blobClient.UploadAsync(uploadFile, true);

            return blobClient.Uri;
        }

    }
}
