using Microsoft.Rest;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace OneNorth.SitecoreTextAnalytics.ContentTagging.Providers
{
    public class AzureCognitiveServicesTextAnalyticsClientCredentials : ServiceClientCredentials
    {
        private readonly string _apiKey;

        public AzureCognitiveServicesTextAnalyticsClientCredentials(string apiKey)
        {
            _apiKey = apiKey;
        }

        public override Task ProcessHttpRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request == null)
                throw new ArgumentNullException("request");

            request.Headers.Add("Ocp-Apim-Subscription-Key", _apiKey);
            return base.ProcessHttpRequestAsync(request, cancellationToken);
        }
    }
}