# PDF Processing with Durable Azure Functions (C# Isolated Worker)

This project demonstrates a high-performance, cost-efficient PDF processing solution using Azure Durable Functions. Designed for scalability and parallelism, it efficiently handles large datasets and runs on a basic consumption plan, achieving significant cost savings.

---

## Features

- **Parallel Processing**: Handles PDF fetching, merging, and uploading in parallel to minimize execution time.
- **Durable Functions**: Orchestrates tasks with built-in error handling and custom status tracking (`Complete`, `Fail`, `Error`).
- **Azure Blob Storage Integration**: Processes PDFs stored by folder in a blob container.
- **Optimized for Cost**: Runs on the basic Azure Functions consumption plan.

---

## How It Works

1. **Trigger the Orchestration**:
   - Call the HTTP endpoint with a folder name to process PDFs in that folder.
   - Example:
     ```
     GET /api/pdfcombine?foldername=<folder-name>
     ```
     Replace `<folder-name>` with the desired folder in the Azure Blob Storage container.
   - Default folder: `DefaultFolderName`.

2. **Orchestration Flow**:
   - Fetch PDFs from the specified folder in Blob Storage.
   - Download and process them in parallel.
   - Generate a Table of Contents (TOC) for the merged PDF.
   - Upload the merged PDF with TOC back to Blob Storage.

---

## Example Use Case

To process PDFs in a folder named `transport-docs`:
GET https://<function-app-name>.azurewebsites.net/api/pdfcombine?foldername=transport-docs

yaml
Copy code

---

## Prerequisites

1. **Azure Storage Account**: A blob container named `pdf-container`.
2. **Environment Variables**:
   - `AzureWebJobsStorage`: Connection string for the Azure Storage Account.
3. **Azure Function App**: Deploy this project to an Azure Function App configured for the Isolated Worker model.

---

## Components

1. **Orchestrator Function**:
   - Coordinates PDF processing using durable function orchestration.

2. **Activity Functions**:
   - Fetches PDFs, generates TOC, merges PDFs, and uploads the final file.

3. **Test Function**:
   - Simulates uploading and processing PDFs for development and testing.

4. **PDF Handling**:
   - Utilizes `Aspose.PDF` for merging and generating TOC.

---

## Technology Stack

- **Azure Durable Functions** (C# Isolated Worker)
- **Azure Blob Storage** (for file storage and operations)
- **Aspose.PDF** (for PDF manipulation)

---

## Results

This solution was demonstrated to a State Department of Transportation to showcase its ability to handle large-scale PDF processing tasks, staying within execution time limits and significantly reducing costs.

---

## Deployment

1. Clone this repository.
2. Configure environment variables in the Azure Function App settings.
3. Deploy to Azure using your preferred deployment method (e.g., Visual Studio, Azure CLI).
