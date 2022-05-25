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

namespace TextAnalyticsForHealthFunction
{
    public static class Function1
    {
        private static readonly string subscriptionKey = "";
        private static readonly string endpoint = "https://westeurope.cognitiveservices.azure.com";

        [FunctionName("TextAnalyticsWorker")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
            ILogger log)
        {

            string query = await new StreamReader(req.Body).ReadToEndAsync();
            var client = new TextAnalyticsClient(new Uri(endpoint), new AzureKeyCredential(subscriptionKey));
            List<string> batchInput = new List<string>() { query };
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
                            var temp = new EntityInfo
                            {
                                Text = entity.Text,
                                Category = entity.Category.ToString(),
                                ConfidenceScore = entity.ConfidenceScore,
                                Snowmed = entity.DataSources.FirstOrDefault(p => p.Name == "SNOMEDCT_US"),
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
        public EntityDataSource Snowmed { get; set; }
        public string Association { get; set; }
        public string Conditionality { get; set; }
        public string Certainty { get; set; }
        public List<string> RelationInfo { get; set; }
        public string NormalizedText { get; set; }
    }
}
