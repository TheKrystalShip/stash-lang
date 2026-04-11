namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Models;
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

        // http.get(url[, options]) — Sends an HTTP GET request. Optionally accepts an options dict with 'headers' (dict) and 'timeout' (int, ms). Returns a HttpResponse struct.
        ns.Function("get", [Param("url", "string"), Param("options", "dict")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 1 || args.Length > 2)
                throw new RuntimeError("'http.get' requires 1 or 2 arguments.");
            var url = SvArgs.String(args, 0, "http.get");

            ValidateUrl(url, "http.get");

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            var timeout = ApplyOptions(request, args, 1, "http.get");
            using var cts = timeout.HasValue ? new CancellationTokenSource(timeout.Value) : null;

            try
            {
                var response = _client.SendAsync(request, cts?.Token ?? CancellationToken.None).GetAwaiter().GetResult();
                return StashValue.FromObj(MakeResponse(response));
            }
            catch (HttpRequestException e)
            {
                throw new RuntimeError("http.get: request failed — " + e.Message);
            }
            catch (TaskCanceledException)
            {
                throw new RuntimeError("http.get: request timed out.");
            }
        }, returnType: "HttpResponse", isVariadic: true,
        documentation: "Sends an HTTP GET request to the given URL. Optionally accepts an options dict with 'headers' (dict of name→value pairs) and 'timeout' (int, milliseconds). Returns an HttpResponse struct with status, body, and headers fields.");

        // http.post(url, body[, options]) — Sends an HTTP POST request with a JSON body string. Optionally accepts an options dict with 'headers' (dict) and 'timeout' (int, ms). Returns a HttpResponse struct.
        ns.Function("post", [Param("url", "string"), Param("body", "string"), Param("options", "dict")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 2 || args.Length > 3)
                throw new RuntimeError("'http.post' requires 2 or 3 arguments.");
            var url = SvArgs.String(args, 0, "http.post");
            var body = SvArgs.String(args, 1, "http.post");

            ValidateUrl(url, "http.post");

            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            };
            var timeout = ApplyOptions(request, args, 2, "http.post");
            using var cts = timeout.HasValue ? new CancellationTokenSource(timeout.Value) : null;

            try
            {
                var response = _client.SendAsync(request, cts?.Token ?? CancellationToken.None).GetAwaiter().GetResult();
                return StashValue.FromObj(MakeResponse(response));
            }
            catch (HttpRequestException e)
            {
                throw new RuntimeError("http.post: request failed — " + e.Message);
            }
            catch (TaskCanceledException)
            {
                throw new RuntimeError("http.post: request timed out.");
            }
        }, returnType: "HttpResponse", isVariadic: true,
        documentation: "Sends an HTTP POST request with a JSON body string. Optionally accepts an options dict with 'headers' (dict of name→value pairs) and 'timeout' (int, milliseconds). Returns an HttpResponse struct with status, body, and headers fields.");

        // http.put(url, body[, options]) — Sends an HTTP PUT request with a JSON body string. Optionally accepts an options dict with 'headers' (dict) and 'timeout' (int, ms). Returns a HttpResponse struct.
        ns.Function("put", [Param("url", "string"), Param("body", "string"), Param("options", "dict")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 2 || args.Length > 3)
                throw new RuntimeError("'http.put' requires 2 or 3 arguments.");
            var url = SvArgs.String(args, 0, "http.put");
            var body = SvArgs.String(args, 1, "http.put");

            ValidateUrl(url, "http.put");

            var request = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            };
            var timeout = ApplyOptions(request, args, 2, "http.put");
            using var cts = timeout.HasValue ? new CancellationTokenSource(timeout.Value) : null;

            try
            {
                var response = _client.SendAsync(request, cts?.Token ?? CancellationToken.None).GetAwaiter().GetResult();
                return StashValue.FromObj(MakeResponse(response));
            }
            catch (HttpRequestException e)
            {
                throw new RuntimeError("http.put: request failed — " + e.Message);
            }
            catch (TaskCanceledException)
            {
                throw new RuntimeError("http.put: request timed out.");
            }
        }, returnType: "HttpResponse", isVariadic: true,
        documentation: "Sends an HTTP PUT request with a JSON body string. Optionally accepts an options dict with 'headers' (dict of name→value pairs) and 'timeout' (int, milliseconds). Returns an HttpResponse struct with status, body, and headers fields.");

        // http.delete(url[, options]) — Sends an HTTP DELETE request. Optionally accepts an options dict with 'headers' (dict) and 'timeout' (int, ms). Returns a HttpResponse struct.
        ns.Function("delete", [Param("url", "string"), Param("options", "dict")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 1 || args.Length > 2)
                throw new RuntimeError("'http.delete' requires 1 or 2 arguments.");
            var url = SvArgs.String(args, 0, "http.delete");

            ValidateUrl(url, "http.delete");

            var request = new HttpRequestMessage(HttpMethod.Delete, url);
            var timeout = ApplyOptions(request, args, 1, "http.delete");
            using var cts = timeout.HasValue ? new CancellationTokenSource(timeout.Value) : null;

            try
            {
                var response = _client.SendAsync(request, cts?.Token ?? CancellationToken.None).GetAwaiter().GetResult();
                return StashValue.FromObj(MakeResponse(response));
            }
            catch (HttpRequestException e)
            {
                throw new RuntimeError("http.delete: request failed — " + e.Message);
            }
            catch (TaskCanceledException)
            {
                throw new RuntimeError("http.delete: request timed out.");
            }
        }, returnType: "HttpResponse", isVariadic: true,
        documentation: "Sends an HTTP DELETE request to the given URL. Optionally accepts an options dict with 'headers' (dict of name→value pairs) and 'timeout' (int, milliseconds). Returns an HttpResponse struct with status, body, and headers fields.");

        // http.request(options) — Sends a fully customizable HTTP request. Options dict supports: url, method, headers (dict), body. Returns a HttpResponse struct.
        ns.Function("request", [Param("options", "dict")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var options = SvArgs.Dict(args, 0, "http.request");

            var urlVal = options.Get("url").ToObject();
            if (urlVal is not string url)
            {
                throw new RuntimeError("http.request: 'url' must be a string.");
            }

            ValidateUrl(url, "http.request");

            var methodVal = options.Get("method").ToObject();
            var methodStr = methodVal is string m ? m.ToUpperInvariant() : "GET";

            var requestMessage = new HttpRequestMessage(new HttpMethod(methodStr), url);

            var headersVal = options.Get("headers").ToObject();
            if (headersVal is StashDictionary headersDict)
            {
                foreach (var key in headersDict.RawKeys())
                {
                    var keyStr = RuntimeValues.Stringify(key);
                    var valStr = RuntimeValues.Stringify(headersDict.Get(key).ToObject());
                    requestMessage.Headers.TryAddWithoutValidation(keyStr, valStr);
                }
            }

            var bodyVal = options.Get("body").ToObject();
            if (bodyVal is string bodyStr)
            {
                requestMessage.Content = new StringContent(bodyStr, System.Text.Encoding.UTF8, "application/json");
            }

            try
            {
                var response = _client.SendAsync(requestMessage).GetAwaiter().GetResult();
                return StashValue.FromObj(MakeResponse(response));
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

        // http.patch(url, body[, options]) — Sends an HTTP PATCH request with a JSON body string. Optionally accepts an options dict with 'headers' (dict) and 'timeout' (int, ms). Returns a HttpResponse struct.
        ns.Function("patch", [Param("url", "string"), Param("body", "string"), Param("options", "dict")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 2 || args.Length > 3)
                throw new RuntimeError("'http.patch' requires 2 or 3 arguments.");
            var url = SvArgs.String(args, 0, "http.patch");
            var body = SvArgs.String(args, 1, "http.patch");

            ValidateUrl(url, "http.patch");

            var request = new HttpRequestMessage(HttpMethod.Patch, url)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            };
            var timeout = ApplyOptions(request, args, 2, "http.patch");
            using var cts = timeout.HasValue ? new CancellationTokenSource(timeout.Value) : null;

            try
            {
                var response = _client.SendAsync(request, cts?.Token ?? CancellationToken.None).GetAwaiter().GetResult();
                return StashValue.FromObj(MakeResponse(response));
            }
            catch (HttpRequestException e)
            {
                throw new RuntimeError("http.patch: request failed — " + e.Message);
            }
            catch (TaskCanceledException)
            {
                throw new RuntimeError("http.patch: request timed out.");
            }
        }, returnType: "HttpResponse", isVariadic: true,
        documentation: "Sends an HTTP PATCH request with a JSON body string. Optionally accepts an options dict with 'headers' (dict of name→value pairs) and 'timeout' (int, milliseconds). Returns an HttpResponse struct with status, body, and headers fields.");

        // http.download(url, path[, options]) — Downloads the response body of a GET request and writes it to the given file path. Optionally accepts an options dict with 'headers' (dict) and 'timeout' (int, ms). Returns null.
        ns.Function("download", [Param("url", "string"), Param("path", "string"), Param("options", "dict")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 2 || args.Length > 3)
                throw new RuntimeError("'http.download' requires 2 or 3 arguments.");
            var url = SvArgs.String(args, 0, "http.download");
            var path = SvArgs.String(args, 1, "http.download");

            ValidateUrl(url, "http.download");
            path = ctx.ExpandTilde(path);

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            var timeout = ApplyOptions(request, args, 2, "http.download");
            using var cts = timeout.HasValue ? new CancellationTokenSource(timeout.Value) : null;

            try
            {
                using var response = _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts?.Token ?? CancellationToken.None).GetAwaiter().GetResult();
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
            return StashValue.Null;
        }, isVariadic: true,
        documentation: "Downloads the response body of a GET request and writes it to the given file path. Optionally accepts an options dict with 'headers' (dict of name→value pairs) and 'timeout' (int, milliseconds). Returns null.");

        ns.Struct("HttpResponse", [
            new BuiltInField("status", "int"),
            new BuiltInField("body", "string"),
            new BuiltInField("headers", "dict"),
        ]);

        return ns.Build();
    }

    /// <summary>
    /// Extracts the optional <c>options</c> dict from <paramref name="args"/> at <paramref name="optionsIndex"/>,
    /// applies any <c>headers</c> to the request message, and returns the requested timeout if provided.
    /// </summary>
    private static TimeSpan? ApplyOptions(HttpRequestMessage request, ReadOnlySpan<StashValue> args, int optionsIndex, string funcName)
    {
        if (args.Length <= optionsIndex) return null;

        var options = SvArgs.Dict(args, optionsIndex, funcName);

        var headersVal = options.Get("headers").ToObject();
        if (headersVal is StashDictionary headersDict)
        {
            foreach (var key in headersDict.RawKeys())
            {
                var keyStr = RuntimeValues.Stringify(key);
                var valStr = RuntimeValues.Stringify(headersDict.Get(key).ToObject());
                request.Headers.TryAddWithoutValidation(keyStr, valStr);
            }
        }

        var timeoutVal = options.Get("timeout").ToObject();
        return timeoutVal switch
        {
            long ms => TimeSpan.FromMilliseconds((double)ms),
            double dms => TimeSpan.FromMilliseconds(dms),
            _ => null
        };
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
            headers.Set(header.Key, StashValue.FromObj(string.Join(", ", header.Value)));
        }

        foreach (var header in response.Content.Headers)
        {
            headers.Set(header.Key, StashValue.FromObj(string.Join(", ", header.Value)));
        }

        return new StashInstance("HttpResponse", new Dictionary<string, StashValue>
        {
            ["status"] = StashValue.FromInt((long)response.StatusCode),
            ["body"] = StashValue.FromObj(body),
            ["headers"] = StashValue.FromObj(headers)
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
