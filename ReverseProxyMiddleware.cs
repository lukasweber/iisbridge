using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace iisbridge {

    public class ReverseProxyMiddleware
    {
        private const int INITIAL_REQUEST_TIMEOUT = 20000;

        private static readonly HttpClient httpClient = new HttpClient(new HttpClientHandler() { UseCookies = false });
        private readonly RequestDelegate nextMiddleware;
        private readonly ReverseProxyOptions options;
        private readonly ExecutableHandler exeHandler;

        public ReverseProxyMiddleware(RequestDelegate nextMiddleware, ReverseProxyOptions options)
        {
            this.nextMiddleware = nextMiddleware;
            this.options = options;
            this.exeHandler = options.ExecutableHandler;
        }

        public async Task Invoke(HttpContext context)
        {
            // Ensure Web-App is started
            if (!exeHandler.Started) 
            {
                exeHandler.Start();
            }
            if (!exeHandler.StartFailed) 
            {
                // Create Source
                var targetUri = BuildTargetUri(context.Request);
                if (targetUri != null)
                {
                    // Create Request for target & send it
                    var targetRequestMessage = CreateTargetMessage(context, targetUri);
                    Console.WriteLine(targetRequestMessage.Headers.ToString());
                    using (var responseMessage = await GetTargetResponse(context, targetRequestMessage))
                    {
                        context.Response.StatusCode = (int)responseMessage.StatusCode;
                        CopyFromTargetResponseHeaders(context, responseMessage);
                        await responseMessage.Content.CopyToAsync(context.Response.Body);
                    }
                    return;
                }
            } 
            else 
            {
                throw new Exception("Start of WebApp Executable failed!");
            }
            await nextMiddleware(context);
        }

        public async Task<HttpResponseMessage> GetTargetResponse(HttpContext context, HttpRequestMessage targetRequestMessage) {
            try
            {
                var response = await httpClient.SendAsync(targetRequestMessage, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
                return response;            
            }
            catch (HttpRequestException ex) 
            {
                // If initial request timeout is not reached yet
                if (exeHandler.StartedTime.HasValue && exeHandler.StartedTime.Value > DateTime.Now.AddMilliseconds(-INITIAL_REQUEST_TIMEOUT)) 
                {
                    await Task.Delay(1000);
                    return await GetTargetResponse(context, Clone(targetRequestMessage));
                } 
                else 
                {
                    throw ex;
                }
            }
        }

        private HttpRequestMessage CreateTargetMessage(HttpContext context, Uri targetUri)
        {
            var requestMessage = new HttpRequestMessage();
            CopyFromOriginalRequestContentAndHeaders(context, requestMessage);

            requestMessage.RequestUri = targetUri;
            requestMessage.Headers.Host = targetUri.Host;
            requestMessage.Method = GetMethod(context.Request.Method);

            // Add user authenticated by Windows Auth
            requestMessage.Headers.Remove("x-auth-user");
            requestMessage.Headers.Add("x-auth-user", context.User.Identity.Name ?? "Unknown");

            return requestMessage;
        }

        private void CopyFromOriginalRequestContentAndHeaders(HttpContext context, HttpRequestMessage requestMessage)
        {
            var requestMethod = context.Request.Method;

            if (!HttpMethods.IsGet(requestMethod) &&
                !HttpMethods.IsHead(requestMethod) &&
                !HttpMethods.IsDelete(requestMethod) &&
                !HttpMethods.IsTrace(requestMethod))
            {
                var streamContent = new StreamContent(context.Request.Body);
                requestMessage.Content = streamContent;
            }

            foreach (var header in context.Request.Headers)
            {
                requestMessage.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }

            foreach (var header in context.Request.Headers)
            {
                requestMessage?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }

        private void CopyFromTargetResponseHeaders(HttpContext context, HttpResponseMessage responseMessage)
        {
            foreach (var header in responseMessage.Headers)
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }

            foreach (var header in responseMessage.Content.Headers)
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }
            context.Response.Headers.Remove("transfer-encoding");
        }
        private static HttpMethod GetMethod(string method)
        {
            if (HttpMethods.IsDelete(method)) return HttpMethod.Delete;
            if (HttpMethods.IsGet(method)) return HttpMethod.Get;
            if (HttpMethods.IsHead(method)) return HttpMethod.Head;
            if (HttpMethods.IsOptions(method)) return HttpMethod.Options;
            if (HttpMethods.IsPost(method)) return HttpMethod.Post;
            if (HttpMethods.IsPut(method)) return HttpMethod.Put;
            if (HttpMethods.IsTrace(method)) return HttpMethod.Trace;
            return new HttpMethod(method);
        }

        private Uri BuildTargetUri(HttpRequest request)
        {
            string requestPath = request.Path.ToString();
            string queryString = request.QueryString.ToString();

            return new Uri(options.BaseUrl + requestPath + queryString);
        }

        public HttpRequestMessage Clone(HttpRequestMessage req)
        {
            HttpRequestMessage clone = new HttpRequestMessage(req.Method, req.RequestUri);

            clone.Content = req.Content;
            clone.Version = req.Version;

            foreach (KeyValuePair<string, object> prop in req.Properties)
            {
                clone.Properties.Add(prop);
            }

            foreach (KeyValuePair<string, IEnumerable<string>> header in req.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return clone;
        }
    }

    public static class ReverseProxyMiddlewareExtensions 
    {
        public static IApplicationBuilder UseReverseProxy(this IApplicationBuilder app, ReverseProxyOptions options)  
        {  
            return app.UseMiddleware<ReverseProxyMiddleware>(options);  
        } 
    }

    public class ReverseProxyOptions 
    {
        public string BaseUrl { get; set; }
        public ExecutableHandler ExecutableHandler { get; set;}
    }
}