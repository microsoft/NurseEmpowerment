using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Azure.AI.TextAnalytics;
using Azure;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection.Metadata;
using System.Net.Http;
using System.Transactions;

namespace TextAnalyticsForHealthFunction
{
    public static class Function1
    {
        private static readonly string GeneralSubscriptionKey = Environment.GetEnvironmentVariable("GENERAL-COGNITIVESERVICE-KEY");
        private static readonly string TranslatorSubscriptionKey = Environment.GetEnvironmentVariable("GENERAL-COGNITIVESERVICE-KEY");
        private static readonly string GeneralCognitivServiceEndPoint = "https://westeurope.cognitiveservices.azure.com";
        private static readonly string TranslatorEndpoint = " https://api.cognitive.microsofttranslator.com";
       
        [FunctionName("TextAnalyticsWorker")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            string query = await new StreamReader(req.Body).ReadToEndAsync();
            string translateRoute = "/translate?api-version=3.0&to=en";
            var translated = await TranslateTextRequest(TranslatorSubscriptionKey, TranslatorEndpoint, translateRoute, query);
            List<string> batchInput = new List<string>() { translated };
            var client = new TextAnalyticsClient(new Uri(GeneralCognitivServiceEndPoint), new AzureKeyCredential(GeneralSubscriptionKey));
            AnalyzeHealthcareEntitiesOperation healthOperation = await client.StartAnalyzeHealthcareEntitiesAsync(batchInput);
            await healthOperation.WaitForCompletionAsync();
            var returnValue = new ProcessedContent();
            await foreach (AnalyzeHealthcareEntitiesResultCollection documentsInPage in healthOperation.Value)
            {
                foreach (AnalyzeHealthcareEntitiesResult entitiesInDoc in documentsInPage)
                {
                    if (!entitiesInDoc.HasError)
                    {
                        foreach (HealthcareEntityRelation relation in entitiesInDoc.EntityRelations)
                        {
                            foreach (HealthcareEntityRelationRole role in relation.Roles)
                            {
                                returnValue.Relations.Add($"{relation.RelationType}: {role.Entity.Text} ({role.Entity.Category})");
                            }
                        }

                        foreach (var entity in entitiesInDoc.Entities)
                        {
                            var snowmed = entity.DataSources.FirstOrDefault(p => p.Name == "SNOMEDCT_US");
                            var temp = new EntityInfo
                            {
                                Text = entity.Text,
                                Category = entity.Category.ToString(),
                                ConfidenceScore = entity.ConfidenceScore,
                                Snowmed = snowmed != null ? snowmed.EntityId :  "",
                                Length = entity.Length,
                                NormalizedText = entity.NormalizedText,
                                Offset = entity.Offset,
                                SubCategory = entity.SubCategory
                            };

                            if (entity.Assertion != null)
                            {
                                if (entity.Assertion?.Association != null)
                                {
                                    temp.Association = entity.Assertion?.Association.Value.ToString();
                                }
                                if (entity.Assertion?.Certainty != null)
                                {
                                    temp.Certainty = entity.Assertion?.Certainty.Value.ToString();
                                }
                                if (entity.Assertion?.Conditionality != null)
                                {
                                    temp.Conditionality = entity.Assertion?.Conditionality.Value.ToString();
                                }
                            }
                            returnValue.Entities.Add(temp);
                        }
                    }
                }
            }
     
            return new OkObjectResult(returnValue);
        }

        static public async Task<string> TranslateTextRequest(string resourceKey, string endpoint, string route, string inputText)
        {
            object[] body = new object[] { new { Text = inputText } };
            var requestBody = JsonConvert.SerializeObject(body);

            using (var client = new HttpClient())
            using (var request = new HttpRequestMessage())
            {
                // Build the request.
                request.Method = HttpMethod.Post;
                request.RequestUri = new Uri(endpoint + route);
                request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
                request.Headers.Add("Ocp-Apim-Subscription-Key", resourceKey);
                request.Headers.Add("Ocp-Apim-Subscription-Region", "westeurope");

                // Send the request and get response.
                HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);
                // Read response as a string.
                string result = await response.Content.ReadAsStringAsync();
                TranslationResult[] deserializedOutput = JsonConvert.DeserializeObject<TranslationResult[]>(result);
                // Iterate over the deserialized results.
                if (deserializedOutput.Any())
                {
                    var o = deserializedOutput.FirstOrDefault();
                    if (o.DetectedLanguage.Language == "en")
                    {
                        return inputText;
                    }
                    if (o.Translations.Any())
                    {
                        return o.Translations.FirstOrDefault().Text;
                    }
                }
                return "";
            }
        }
    }

    

    public class ProcessedContent
    {
        public ProcessedContent()
        {
            Entities = new List<EntityInfo>();
            Relations = new List<string>();
        }
        public List<EntityInfo> Entities { get; set; }
        public List<string> Relations { get; set; }

    }
    public class EntityInfo
    {
        public string Text { get; set; }
        public string Category { get; set; }
        public string SubCategory { get; set; }
        public double ConfidenceScore { get; set; }
        public int Offset { get; set; }
        public int Length { get; set; }
        public string Snowmed { get; set; }
        public string Association { get; set; }
        public string Conditionality { get; set; }
        public string Certainty { get; set; }
        public List<string> RelationInfo { get; set; }
        public string NormalizedText { get; set; }
    }

    public class TranslationResult
    {
        public DetectedLanguage DetectedLanguage { get; set; }
        public TextResult SourceText { get; set; }
        public Translation[] Translations { get; set; }
    }

    public class DetectedLanguage
    {
        public string Language { get; set; }
        public float Score { get; set; }
    }

    public class TextResult
    {
        public string Text { get; set; }
        public string Script { get; set; }
    }

    public class Translation
    {
        public string Text { get; set; }
        public TextResult Transliteration { get; set; }
        public string To { get; set; }
        public Alignment Alignment { get; set; }
        public SentenceLength SentLen { get; set; }
    }

    public class Alignment
    {
        public string Proj { get; set; }
    }

    public class SentenceLength
    {
        public int[] SrcSentLen { get; set; }
        public int[] TransSentLen { get; set; }
    }

}
