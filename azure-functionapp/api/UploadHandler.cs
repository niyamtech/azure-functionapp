using System.Net;
using System.Text;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

public class UploadHandler
{
    private readonly BlobServiceClient _blobServiceClient;

    public UploadHandler()
    {
        _blobServiceClient = new BlobServiceClient(
            Environment.GetEnvironmentVariable("AzureWebJobsStorage"));
    }

    [Function("UploadHandler")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
    {
        var contentType = req.Headers.FirstOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)).Value?.FirstOrDefault();
        if (string.IsNullOrEmpty(contentType) || !contentType.Contains("multipart/form-data"))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync("Expected multipart/form-data request.");
            return bad;
        }

        var boundary = HeaderUtilities.RemoveQuotes(MediaTypeHeaderValue.Parse(contentType).Boundary).Value;
        if (string.IsNullOrWhiteSpace(boundary))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync("Missing multipart boundary.");
            return bad;
        }

        var reader = new MultipartReader(boundary, req.Body);
        MultipartSection? section;
        var container = _blobServiceClient.GetBlobContainerClient("uploads");
        await container.CreateIfNotExistsAsync();

        while ((section = await reader.ReadNextSectionAsync()) != null)
        {
            if (ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition))
            {
                if (disposition.DispositionType.Equals("form-data") && 
                    !string.IsNullOrEmpty(disposition.FileName.Value))
                {
                    var fileName = disposition.FileName.Value;
                    var blob = container.GetBlobClient(fileName);
                    await blob.UploadAsync(section.Body, overwrite: true);
                }
            }
        }

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteStringAsync("✅ File uploaded successfully!");
        return resp;
    }
}
