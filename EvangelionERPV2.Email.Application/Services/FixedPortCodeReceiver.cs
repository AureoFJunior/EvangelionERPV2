using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Auth.OAuth2;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace EvangelionERPV2.EmailModule.Application.Services
{
    public class FixedPortEdgeReceiver : ICodeReceiver
    {
        private readonly int _port;

        public FixedPortEdgeReceiver(int port)
        {
            _port = port;
        }

        public string RedirectUri => $"http://localhost:{_port}/";

        public async Task<AuthorizationCodeResponseUrl> ReceiveCodeAsync(AuthorizationCodeRequestUrl url, CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                Arguments = $"--inprivate \"{url.Build().AbsoluteUri}\"",
                UseShellExecute = true
            };
            Process.Start(startInfo);

            using (var listener = new HttpListener())
            {
                listener.Prefixes.Add(RedirectUri);
                listener.Start();

                var context = await listener.GetContextAsync();
                var response = context.Response;

                string responseString = "<html><body>Login concluído. Pode fechar esta aba.</body></html>";
                var buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.OutputStream.Close();

                var query = context.Request.Url?.Query ?? string.Empty;
                if (string.IsNullOrEmpty(query) || !query.Contains("code="))
                    throw new InvalidOperationException("Authorization code not found in the redirect URI.");

                var queryString = query.StartsWith("?") ? query.Substring(1) : query;

                return new AuthorizationCodeResponseUrl(queryString);
            }
        }
    }
}
