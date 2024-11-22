using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net;
using System.Text;

namespace actDurableIsolated
{
    public static class Function
    {
        [Function(nameof(Function))]
        public static async Task<HttpResponseMessage> RunOrchestrator(
            [OrchestrationTrigger] TaskOrchestrationContext context, ILogger log)
        {
            var outputs = new List<string>();

            try
            {
                DateTime dueTime = context.CurrentUtcDateTime.AddSeconds(10);
                await context.CreateTimer(dueTime, CancellationToken.None);

                // Rename the Durable Activity Function from 'SayHello' to an undefined Function to test error and custom message and status.
                outputs.Add(await context.CallActivityAsync<string>("SayHello", "Tokyo"));
                outputs.Add(await context.CallActivityAsync<string>("SayHello", "Seattle"));
                outputs.Add(await context.CallActivityAsync<string>("SayHello", "London"));


                // Setting custom status to "Completed"
                context.SetCustomStatus("Custom->Complete");

                // If everything is successful, return a 200 OK status.
                HttpResponseMessage httpResponseMessage = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(outputs), Encoding.UTF8, "application/json")
                };
                return httpResponseMessage;
            }
            catch (TaskFailedException ex)
            {
                log.LogError($"Function failed with exception: {ex.Message}");

                // Setting custom status to "Failed"
                context.SetCustomStatus("Custom->Fail");

                // If a function fails, return a 500 Internal Server Error status.
                return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent($"Function execution failed: {ex.Message}", Encoding.UTF8, "application/json")
                };
            }
            catch (Exception ex)
            {
                log.LogError($"An error occurred: {ex.Message}");

                // Setting custom status to "Error"
                context.SetCustomStatus("Custom->Error");

                // For other types of exceptions, you might choose to return a 400 Bad Request or another appropriate status.
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent($"An error occurred: {ex.Message}", Encoding.UTF8, "application/json")
                };
            }
        }

        [Function(nameof(SayHello))]
        public static string SayHello([ActivityTrigger] string name, FunctionContext executionContext)
        {
            ILogger logger = executionContext.GetLogger("SayHello");
            logger.LogInformation("Saying hello to {name}.", name);
            return $"Hello {name}!";
        }

        [Function("Function_HttpStart")]
        public static async Task<HttpResponseData> HttpStart(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestData req,
            [DurableClient] DurableTaskClient client,
            FunctionContext executionContext)
        {
            ILogger logger = executionContext.GetLogger("Function_HttpStart");

            // Function input comes from the request content.
            string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(
                nameof(Function));

            logger.LogInformation("Started orchestration with ID = '{instanceId}'.", instanceId);

            // Returns an HTTP 202 response with an instance management payload.
            // See https://learn.microsoft.com/azure/azure-functions/durable/durable-functions-http-api#start-orchestration
            return client.CreateCheckStatusResponse(req, instanceId);
        }
    }
}
