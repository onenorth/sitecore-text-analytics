using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Linq;
using Sitecore.ContentTagging.Core.Messaging;
using Sitecore.ContentTagging.Core.Models;
using Sitecore.ContentTagging.Core.Providers;
using Sitecore.DependencyInjection;
using Sitecore.Abstractions;
using Microsoft.Azure.CognitiveServices.Language.TextAnalytics;
using Microsoft.Azure.CognitiveServices.Language.TextAnalytics.Models;
using System;
using System.Xml;
using System.Collections.ObjectModel;
using OneNorth.SitecoreTextAnalytics.Models;
using System.Text.RegularExpressions;

namespace OneNorth.SitecoreTextAnalytics.ContentTagging.Providers
{
    /// <summary>
    /// Azure Text Analytics service - following this sample https://docs.microsoft.com/en-us/azure/cognitive-services/text-analytics/quickstarts/csharp
    /// </summary>
    public class AzureCognitiveServicesTextAnalyticsNamedEntitiesDiscoveryProvider : MessageSource, IDiscoveryProvider
    {
        private readonly BaseFactory _baseFactory;
        private readonly BaseSettings _baseSettings;
        private string _endpoint { get; set; }
        private List<string> _exclude { get; set; }
        private List<EntityNameReplacement> _entityNameReplacements;
        private List<string> _includeOther { get; set; }
        private List<string> _includeQuantity { get; set; }
        private string _key { get; set; }


        protected string Endpoint
        {
            get
            {
                if (_endpoint == null)
                    _endpoint = _baseSettings.GetSetting("AzureCognitiveServicesTextAnalytics.Endpoint", "https://westus.api.cognitive.microsoft.com");
                return _endpoint;
            }
        }


        protected virtual List<string> Exclude
        {
            get
            {
                if (_exclude == null)
                    _exclude = PipeSeparatedStringToList(_baseSettings.GetSetting("AzureCognitiveServicesTextAnalytics.NamedEntities.Exclude"));
                return _exclude;
            }
        }

        protected virtual List<string> IncludeOther
        {
            get
            {
                if (_includeOther == null)
                    _includeOther = PipeSeparatedStringToList(_baseSettings.GetSetting("AzureCognitiveServicesTextAnalytics.NamedEntities.IncludeOther"));
                return _includeOther;
            }
        }

        protected virtual List<string> IncludeQuantity
        {
            get
            {
                if (_includeQuantity == null)
                    _includeQuantity = PipeSeparatedStringToList(_baseSettings.GetSetting("AzureCognitiveServicesTextAnalytics.NamedEntities.IncludeQuantity"));
                return _includeQuantity;
            }
        }

        protected string Key
        {
            get
            {
                if (_key == null)
                    _key = _baseSettings.GetSetting("AzureCognitiveServicesTextAnalytics.Key");
                return _key;
            }
        }

        protected List<EntityNameReplacement> EntityNameReplacements
        {
            get
            {
                if (_entityNameReplacements == null)
                {
                    _entityNameReplacements = new List<EntityNameReplacement>();

                    var replaceNodes = _baseFactory.GetConfigNodes("contentTagging/azureCognitiveServicesTextAnalyticsNamedEntities/entityNameReplacements/replace");
                    foreach (XmlNode replaceNode in replaceNodes)
                    {
                        var patternAttribute = replaceNode.Attributes["pattern"];
                        var replacementAttribute = replaceNode.Attributes["replacement"];
                        if (patternAttribute != null && replacementAttribute != null)
                        {
                            var pattern = patternAttribute.Value;
                            var replacement = replacementAttribute.Value;

                            var entityNameReplacement = new EntityNameReplacement();
                            entityNameReplacement.Replacement = replacement;
                            entityNameReplacement.Regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase, new TimeSpan(0, 0, 2));

                            _entityNameReplacements.Add(entityNameReplacement);
                        }
                    }
                }
                return _entityNameReplacements;
            }
        }


        /// <summary>
        /// Constructor
        /// </summary>
        public AzureCognitiveServicesTextAnalyticsNamedEntitiesDiscoveryProvider()
        {
            _baseFactory = ServiceLocator.ServiceProvider.GetService<BaseFactory>();
            _baseSettings = ServiceLocator.ServiceProvider.GetService<BaseSettings>();
        }

        public IEnumerable<TagData> GetTags(IEnumerable<TaggableContent> taggableContents)
        {
            try
            {
                var credentials = new AzureCognitiveServicesTextAnalyticsClientCredentials(Key);
                var client = new TextAnalyticsClient(credentials)
                {
                    Endpoint = Endpoint
                };

                //All of the Text Analytics API endpoints accept raw text data.The current limit is 5,120 characters for each document; 
                //Limit Value
                //Maximum size of a single document   5,120 characters as measured by StringInfo.LengthInTextElements.
                //Maximum size of entire request  1 MB
                //Maximum number of documents in a request    1,000 documents
                var documents = new List<MultiLanguageInput>();
                int id = 0;
                foreach (StringContent taggableContent in taggableContents)
                {
                    var text = (taggableContent != null) ? taggableContent.Content : null;
                    if (string.IsNullOrEmpty(text))
                        continue;

                    if (text.Length > 5100)
                        text = text.Substring(0, 5100);

                    //just skip it if description is 500 characters or less, not enough content to process
                    //if (text.Length > 500)
                    documents.Add(new MultiLanguageInput() { Id = id.ToString(), Text = text });
                    id++;
                }

                if (!documents.Any())
                    return new List<TagData>();

                var inputDocuments = new MultiLanguageBatchInput(documents);
                var results = client.EntitiesBatch(inputDocuments);

                if (results.Errors.Count > 0)
                {
                    var messageBus = MessageBus;
                    if (messageBus != null)
                    {
                        foreach (var error in results.Errors)
                        {
                            messageBus.SendMessage(new Message()
                            {
                                Body = string.Format("Azure Cogntive Services Text Analytics Error: {0} Message: {1}", error.Id, error.Message),
                                Level = MessageLevel.Error
                            });


                        }
                    }
                }

                var tagData = new List<TagData>();
                var entityNames = new List<string>();
                foreach (var document in results.Documents)
                {
                    foreach (var entity in document.Entities)
                    {
                        if (!Exclude.Contains(entity.Name.ToLower()) &&
                            (entity.Type == "Organization" || entity.Type == "Location" ||
                            (entity.Type == "Other" && IncludeOther.Contains(entity.Name.ToLower())) ||
                            (entity.Type == "Quantity" && IncludeQuantity.Contains(entity.Name))))
                        {
                            var name = NormalizeEntityName(entity.Name);
                            if (entity.SubType != null)
                                entityNames.Add(string.Format("{0} - {1} - {2}", entity.Type, entity.SubType, name));
                            else
                                entityNames.Add(string.Format("{0} - {1}", entity.Type, name));
                        }
                    }
                }

                return entityNames.Distinct().Select(x => new TagData() { TagName = x });
            }
            catch(Exception ex)
            {
                var messageBus = MessageBus;
                if (messageBus != null)
                {
                    messageBus.SendMessage(new Message()
                    {
                        Body = string.Format("Azure Cogntive Services Text Analytics Error: {0}", ex.Message),
                        Level = MessageLevel.Error
                    });
                }
            }

            return new List<TagData>();
        }

        public bool IsConfigured()
        {
            //return true;
            return (!string.IsNullOrEmpty(Key));
        }

        private List<string> PipeSeparatedStringToList(string value)
        {
            if (string.IsNullOrEmpty(value))
                return new List<string>();
            return value.Replace(Environment.NewLine, "").Split(new char[] { '|' }, System.StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim().ToLower()).ToList();
        }

        private string NormalizeEntityName(string name)
        {
            var normalizedName = name;

            var entityNameReplacements = EntityNameReplacements;
            foreach(var entityNameReplacement in entityNameReplacements)
            {
                normalizedName = entityNameReplacement.Regex.Replace(normalizedName, entityNameReplacement.Replacement);
            }

            return normalizedName.Trim();
        }
    }
}