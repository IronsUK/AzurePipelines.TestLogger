using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;

namespace AzurePipelines.TestLogger
{
    public class AzureAuthenticationHandler : DelegatingHandler
    {
        private readonly TokenCredential _credential;
        private readonly string[] _scopes;

        public AzureAuthenticationHandler(TokenCredential credential, string[] scopes)
        {
            _credential = credential ?? throw new ArgumentNullException(nameof(credential));
            _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            TokenRequestContext tokenRequestContext = new TokenRequestContext(_scopes);
            AccessToken accessToken = await _credential.GetTokenAsync(tokenRequestContext, cancellationToken);

            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken.Token);

            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            return await base.SendAsync(request, cancellationToken);
        }
    }
}