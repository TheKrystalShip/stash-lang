namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

/// <summary>
/// Registers the <c>http</c> namespace built-in functions for HTTP client operations.
/// </summary>
/// <remarks>
/// <para>
/// Provides functions for making HTTP requests: <c>http.get</c>, <c>http.post</c>,
/// <c>http.put</c>, <c>http.patch</c>, <c>http.delete</c>, <c>http.request</c>,
/// and <c>http.download</c>.
/// </para>
/// <para>
/// All request functions return a <c>HttpResponse</c> struct instance with <c>status</c>,
/// <c>body</c>, and <c>headers</c> fields. This namespace is only registered when the
/// <see cref="StashCapabilities.Network"/> capability is enabled.
/// </para>
/// </remarks>
public static class HttpBuiltIns
{
    /// <summary>
    /// Shared HTTP client instance with a 30-second timeout. Reused across all requests for connection pooling.
    /// </summary>
    private static readonly HttpClient _client = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    /// <summary>
    /// Registers all <c>http</c> namespace functions into the global environment.
    /// </summary>
    /// <param name="globals">The global <see cref="Stash.Interpreting.Environment"/> to register functions in.</param>
    public static NamespaceDefinition Define()
    {
        var ns = new NamespaceBuilder("http");
        ns.RequiresCapability(StashCapabilities.Network);

        // http.get(url) — Sends an HTTP GET request. Returns a HttpResponse struct with status, body, and headers.
        ns.Function("get", [Param("url", "string")], (_, args) =>
        {
            if (args[0] is not string url)
            {
                throw new RuntimeError("First argument to 'http.get' must be a string.");
            }

            ValidateUrl(url, "http.get");

            try
            {
                var response = _client.GetAsync(url).GetAwaiter().GetResult();
                return MakeResponse(response);
            }
            catch (HttpRequestException e)
            {
                throw new RuntimeError("http.get: request failed — " + e.Message);
            }
            catch (TaskCanceledException)
            {
                throw new RuntimeError("http.get: request timed out.");
            }
        }, returnType: "HttpResponse");

        // http.post(url, body) — Sends an HTTP POST request with a JSON body string. Returns a HttpResponse struct.
        ns.Function("post", [Param("url", "string"), Param("body", "string")], (_, args) =>
        {
            if (args[0] is not string url)
            {
                throw new RuntimeError("First argument to 'http.post' must be a string.");
            }

            if (args[1] is not string body)
            {
                throw new RuntimeError("Second argument to 'http.post' must be a string.");
            }

            ValidateUrl(url, "http.post");

            try
            {
                var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
                var response = _client.PostAsync(url, content).GetAwaiter().GetResult();
                return MakeResponse(response);
            }
            catch (HttpRequestException e)
            {
                throw new RuntimeError("http.post: request failed — " + e.Message);
            }
            catch (TaskCanceledException)
            {
                throw new RuntimeError("http.post: request timed out.");
            }
        }, returnType: "HttpResponse");

        // http.put(url, body) — Sends an HTTP PUT request with a JSON body string. Returns a HttpResponse struct.
        ns.Function("put", [Param("url", "string"), Param("body", "string")], (_, args) =>
        {
            if (args[0] is not string url)
            {
                throw new RuntimeError("First argument to 'http.put' must be a string.");
            }

            if (args[1] is not string body)
            {
                throw new RuntimeError("Second argument to 'http.put' must be a string.");
            }

            ValidateUrl(url, "http.put");

            try
            {
                var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
                var response = _client.PutAsync(url, content).GetAwaiter().GetResult();
                return MakeResponse(response);
            }
            catch (HttpRequestException e)
            {
                throw new RuntimeError("http.put: request failed — " + e.Message);
            }
            catch (TaskCanceledException)
            {
                throw new RuntimeError("http.put: request timed out.");
            }
        }, returnType: "HttpResponse");

        // http.delete(url) — Sends an HTTP DELETE request. Returns a HttpResponse struct with status, body, and headers.
        ns.Function("delete", [Param("url", "string")], (_, args) =>
        {
            if (args[0] is not string url)
            {
                throw new RuntimeError("First argument to 'http.delete' must be a string.");
            }

            ValidateUrl(url, "http.delete");

            try
            {
                var response = _client.DeleteAsync(url).GetAwaiter().GetResult();
                return MakeResponse(response);
            }
            catch (HttpRequestException e)
            {
                throw new RuntimeError("http.delete: request failed — " + e.Message);
            }
            catch (TaskCanceledException)
            {
                throw new RuntimeError("http.delete: request timed out.");
            }
        }, returnType: "HttpResponse");

        // http.request(options) — Sends a fully customizable HTTP request. Options dict supports: url, method, headers (dict), body. Returns a HttpResponse struct.
        ns.Function("request", [Param("options", "dict")], (_, args) =>
        {
            if (args[0] is not StashDictionary options)
            {
                throw new RuntimeError("First argument to 'http.request' must be a dict.");
            }

            var urlVal = options.Get("url");
            if (urlVal is not string url)
            {
                throw new RuntimeError("http.request: 'url' must be a string.");
            }

            ValidateUrl(url, "http.request");

            var methodVal = options.Get("method");
            var methodStr = methodVal is string m ? m.ToUpperInvariant() : "GET";

            var requestMessage = new HttpRequestMessage(new HttpMethod(methodStr), url);

            var headersVal = options.Get("headers");
            if (headersVal is StashDictionary headersDict)
            {
                foreach (var key in headersDict.Keys())
                {
                    var keyStr = RuntimeValues.Stringify(key);
                    var valStr = RuntimeValues.Stringify(headersDict.Get(key!));
                    requestMessage.Headers.TryAddWithoutValidation(keyStr, valStr);
                }
            }

            var bodyVal = options.Get("body");
            if (bodyVal is string bodyStr)
            {
                requestMessage.Content = new StringContent(bodyStr, System.Text.Encoding.UTF8, "application/json");
            }

            try
            {
                var response = _client.SendAsync(requestMessage).GetAwaiter().GetResult();
                return MakeResponse(response);
            }
            catch (HttpRequestException e)
            {
                throw new RuntimeError("http.request: request failed — " + e.Message);
            }
            catch (TaskCanceledException)
            {
                throw new RuntimeError("http.request: request timed out.");
            }
        }, returnType: "HttpResponse");

        // http.patch(url, body) — Sends an HTTP PATCH request with a JSON body string. Returns a HttpResponse struct.
        ns.Function("patch", [Param("url", "string"), Param("body", "string")], (_, args) =>
        {
            if (args[0] is not string url)
            {
                throw new RuntimeError("First argument to 'http.patch' must be a string.");
            }

            if (args[1] is not string body)
            {
                throw new RuntimeError("Second argument to 'http.patch' must be a string.");
            }

            ValidateUrl(url, "http.patch");

            try
            {
                var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
                var response = _client.PatchAsync(url, content).GetAwaiter().GetResult();
                return MakeResponse(response);
            }
            catch (HttpRequestException e)
            {
                throw new RuntimeError("http.patch: request failed — " + e.Message);
            }
            catch (TaskCanceledException)
            {
                throw new RuntimeError("http.patch: request timed out.");
            }
        }, returnType: "HttpResponse");

        // http.download(url, path) — Downloads the response body of a GET request and writes it to the given file path. Returns null.
        ns.Function("download", [Param("url", "string"), Param("path", "string")], (ctx, args) =>
        {
            if (args[0] is not string url)
            {
                throw new RuntimeError("First argument to 'http.download' must be a string.");
            }

            if (args[1] is not string path)
            {
                throw new RuntimeError("Second argument to 'http.download' must be a string.");
            }

            ValidateUrl(url, "http.download");
            path = ctx.ExpandTilde(path);

            try
            {
                using var response = _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();
                using var stream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
                using var fileStream = System.IO.File.Create(path);
                stream.CopyTo(fileStream);
            }
            catch (HttpRequestException e)
            {
                throw new RuntimeError("http.download: request failed — " + e.Message);
            }
            catch (TaskCanceledException)
            {
                throw new RuntimeError("http.download: request timed out.");
            }
            catch (System.IO.IOException e)
            {
                try { System.IO.File.Delete(path); } catch { }
                throw new RuntimeError($"http.download: cannot write file '{path}': {e.Message}");
            }
            return null;
        });

        return ns.Build();
    }

    /// <summary>
    /// Converts an <see cref="HttpResponseMessage"/> into a Stash <see cref="StashInstance"/>
    /// with status, body, and headers fields.
    /// </summary>
    /// <param name="response">The HTTP response message to convert.</param>
    /// <returns>A <see cref="StashInstance"/> with <c>status</c>, <c>body</c>, and <c>headers</c> fields.</returns>
    private static StashInstance MakeResponse(HttpResponseMessage response)
    {
        var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        var headers = new StashDictionary();

        foreach (var header in response.Headers)
        {
            headers.Set(header.Key, string.Join(", ", header.Value));
        }

        foreach (var header in response.Content.Headers)
        {
            headers.Set(header.Key, string.Join(", ", header.Value));
        }

        return new StashInstance("HttpResponse", new Dictionary<string, object?>
        {
            ["status"] = (long)response.StatusCode,
            ["body"] = body,
            ["headers"] = headers
        });
    }

    /// <summary>
    /// Validates that a URL uses the <c>http://</c> or <c>https://</c> scheme to prevent SSRF attacks.
    /// </summary>
    /// <param name="url">The URL to validate.</param>
    /// <param name="funcName">The calling function name, used in error messages.</param>
    private static void ValidateUrl(string url, string funcName)
    {
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            throw new RuntimeError($"{funcName}: URL must use http:// or https:// scheme.");
        }
    }
}
