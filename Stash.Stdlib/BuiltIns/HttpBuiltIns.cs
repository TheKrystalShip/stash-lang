namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Abstractions;

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
[StashNamespace(Capability = StashCapabilities.Network)]
public static partial class HttpBuiltIns
{
    /// <summary>HTTP response with status code, body, and headers.</summary>
    [StashStruct]
    public sealed record HttpResponse
    {
        public long Status { get; init; }
        public string Body { get; init; } = "";
        [StashField(Type = "dict")]
        public StashDictionary Headers { get; init; } = new();
    }

    /// <summary>
    /// Shared HTTP client instance with a 30-second timeout. Reused across all requests for connection pooling.
    /// </summary>
    private static readonly HttpClient _client = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    /// <summary>Sends an HTTP GET request to the given URL. Optionally accepts an options dict with 'headers' (dict of name→value pairs) and 'timeout' (int, milliseconds). Returns an HttpResponse struct with status, body, and headers fields.</summary>
    /// <param name="url">The URL to send the GET request to</param>
    /// <param name="options">An optional dict with 'headers' (dict) and/or 'timeout' (int, milliseconds)</param>
    /// <returns>An HttpResponse struct with status (int), body (string), and headers (dict) fields</returns>
    [StashFn(ReturnType = "HttpResponse")]
    private static StashValue Get(IInterpreterContext ctx, string url, params StashValue[] options)
    {
        ValidateUrl(url, "http.get");

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        var timeout = ApplyOptionsValue(request, options.Length > 0 ? options[0] : StashValue.Null, "http.get");
        using var cts = MakeLinkedCts(ctx.CancellationToken, timeout);
        var requestCt = cts?.Token ?? ctx.CancellationToken;

        try
        {
            var response = _client.SendAsync(request, requestCt).GetAwaiter().GetResult();
            return StashValue.FromObj(MakeResponse(response));
        }
        catch (HttpRequestException e)
        {
            throw new RuntimeError("http.get: request failed — " + e.Message, errorType: StashErrorTypes.IOError);
        }
        catch (TaskCanceledException)
        {
            throw new RuntimeError("http.get: request timed out.", errorType: StashErrorTypes.TimeoutError);
        }
    }

    /// <summary>Sends an HTTP POST request with a JSON body string. Optionally accepts an options dict with 'headers' (dict of name→value pairs) and 'timeout' (int, milliseconds). Returns an HttpResponse struct with status, body, and headers fields.</summary>
    /// <param name="url">The URL to send the POST request to</param>
    /// <param name="body">The request body string (typically JSON)</param>
    /// <param name="options">An optional dict with 'headers' (dict) and/or 'timeout' (int, milliseconds)</param>
    /// <returns>An HttpResponse struct with status (int), body (string), and headers (dict) fields</returns>
    [StashFn(ReturnType = "HttpResponse")]
    private static StashValue Post(IInterpreterContext ctx, string url, string body, params StashValue[] options)
    {
        ValidateUrl(url, "http.post");

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
        var timeout = ApplyOptionsValue(request, options.Length > 0 ? options[0] : StashValue.Null, "http.post");
        using var cts = MakeLinkedCts(ctx.CancellationToken, timeout);
        var requestCt = cts?.Token ?? ctx.CancellationToken;

        try
        {
            var response = _client.SendAsync(request, requestCt).GetAwaiter().GetResult();
            return StashValue.FromObj(MakeResponse(response));
        }
        catch (HttpRequestException e)
        {
            throw new RuntimeError("http.post: request failed — " + e.Message, errorType: StashErrorTypes.IOError);
        }
        catch (TaskCanceledException)
        {
            throw new RuntimeError("http.post: request timed out.", errorType: StashErrorTypes.TimeoutError);
        }
    }

    /// <summary>Sends an HTTP PUT request with a JSON body string. Optionally accepts an options dict with 'headers' (dict of name→value pairs) and 'timeout' (int, milliseconds). Returns an HttpResponse struct with status, body, and headers fields.</summary>
    /// <param name="url">The URL to send the PUT request to</param>
    /// <param name="body">The request body string (typically JSON)</param>
    /// <param name="options">An optional dict with 'headers' (dict) and/or 'timeout' (int, milliseconds)</param>
    /// <returns>An HttpResponse struct with status (int), body (string), and headers (dict) fields</returns>
    [StashFn(ReturnType = "HttpResponse")]
    private static StashValue Put(IInterpreterContext ctx, string url, string body, params StashValue[] options)
    {
        ValidateUrl(url, "http.put");

        var request = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
        var timeout = ApplyOptionsValue(request, options.Length > 0 ? options[0] : StashValue.Null, "http.put");
        using var cts = MakeLinkedCts(ctx.CancellationToken, timeout);
        var requestCt = cts?.Token ?? ctx.CancellationToken;

        try
        {
            var response = _client.SendAsync(request, requestCt).GetAwaiter().GetResult();
            return StashValue.FromObj(MakeResponse(response));
        }
        catch (HttpRequestException e)
        {
            throw new RuntimeError("http.put: request failed — " + e.Message, errorType: StashErrorTypes.IOError);
        }
        catch (TaskCanceledException)
        {
            throw new RuntimeError("http.put: request timed out.", errorType: StashErrorTypes.TimeoutError);
        }
    }

    /// <summary>Sends an HTTP DELETE request to the given URL. Optionally accepts an options dict with 'headers' (dict of name→value pairs) and 'timeout' (int, milliseconds). Returns an HttpResponse struct with status, body, and headers fields.</summary>
    /// <param name="url">The URL to send the DELETE request to</param>
    /// <param name="options">An optional dict with 'headers' (dict) and/or 'timeout' (int, milliseconds)</param>
    /// <returns>An HttpResponse struct with status (int), body (string), and headers (dict) fields</returns>
    [StashFn(ReturnType = "HttpResponse")]
    private static StashValue Delete(IInterpreterContext ctx, string url, params StashValue[] options)
    {
        ValidateUrl(url, "http.delete");

        var request = new HttpRequestMessage(HttpMethod.Delete, url);
        var timeout = ApplyOptionsValue(request, options.Length > 0 ? options[0] : StashValue.Null, "http.delete");
        using var cts = MakeLinkedCts(ctx.CancellationToken, timeout);
        var requestCt = cts?.Token ?? ctx.CancellationToken;

        try
        {
            var response = _client.SendAsync(request, requestCt).GetAwaiter().GetResult();
            return StashValue.FromObj(MakeResponse(response));
        }
        catch (HttpRequestException e)
        {
            throw new RuntimeError("http.delete: request failed — " + e.Message, errorType: StashErrorTypes.IOError);
        }
        catch (TaskCanceledException)
        {
            throw new RuntimeError("http.delete: request timed out.", errorType: StashErrorTypes.TimeoutError);
        }
    }

    /// <summary>Sends an HTTP HEAD request and returns the response status and headers.</summary>
    /// <param name="url">The URL to request</param>
    /// <param name="options">Optional request options (headers, timeout)</param>
    /// <returns>HttpResponse with status, headers, and empty body</returns>
    [StashFn(ReturnType = "HttpResponse")]
    private static StashValue Head(IInterpreterContext ctx, string url, params StashValue[] options)
    {
        ValidateUrl(url, "http.head");

        var request = new HttpRequestMessage(HttpMethod.Head, url);
        var timeout = ApplyOptionsValue(request, options.Length > 0 ? options[0] : StashValue.Null, "http.head");
        using var cts = MakeLinkedCts(ctx.CancellationToken, timeout);
        var requestCt = cts?.Token ?? ctx.CancellationToken;

        try
        {
            var response = _client.SendAsync(request, requestCt).GetAwaiter().GetResult();
            return StashValue.FromObj(MakeResponse(response));
        }
        catch (HttpRequestException e)
        {
            throw new RuntimeError("http.head: request failed — " + e.Message, errorType: StashErrorTypes.IOError);
        }
        catch (TaskCanceledException)
        {
            throw new RuntimeError("http.head: request timed out.", errorType: StashErrorTypes.TimeoutError);
        }
    }

    /// <summary>Sends a custom HTTP request. The options dict must include 'url' (string) and optionally 'method' (string, default GET), 'headers' (dict), and 'body' (string). Returns an HttpResponse struct with status, body, and headers fields.</summary>
    /// <param name="options">A dict with request options: url, method, headers, body</param>
    /// <returns>An HttpResponse struct with status, body, and headers</returns>
    [StashFn(ReturnType = "HttpResponse")]
    private static StashValue Request(IInterpreterContext ctx, StashDictionary options)
    {
        var urlVal = options.Get("url").ToObject();
        if (urlVal is not string url)
        {
            throw new RuntimeError("http.request: 'url' must be a string.", errorType: StashErrorTypes.TypeError);
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
            var response = _client.SendAsync(requestMessage, ctx.CancellationToken).GetAwaiter().GetResult();
            return StashValue.FromObj(MakeResponse(response));
        }
        catch (HttpRequestException e)
        {
            throw new RuntimeError("http.request: request failed — " + e.Message, errorType: StashErrorTypes.IOError);
        }
        catch (TaskCanceledException)
        {
            throw new RuntimeError("http.request: request timed out.", errorType: StashErrorTypes.TimeoutError);
        }
    }

    /// <summary>Sends an HTTP PATCH request with a JSON body string. Optionally accepts an options dict with 'headers' (dict of name→value pairs) and 'timeout' (int, milliseconds). Returns an HttpResponse struct with status, body, and headers fields.</summary>
    /// <param name="url">The URL to send the PATCH request to</param>
    /// <param name="body">The request body string (typically JSON)</param>
    /// <param name="options">An optional dict with 'headers' (dict) and/or 'timeout' (int, milliseconds)</param>
    /// <returns>An HttpResponse struct with status (int), body (string), and headers (dict) fields</returns>
    [StashFn(ReturnType = "HttpResponse")]
    private static StashValue Patch(IInterpreterContext ctx, string url, string body, params StashValue[] options)
    {
        ValidateUrl(url, "http.patch");

        var request = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
        var timeout = ApplyOptionsValue(request, options.Length > 0 ? options[0] : StashValue.Null, "http.patch");
        using var cts = MakeLinkedCts(ctx.CancellationToken, timeout);
        var requestCt = cts?.Token ?? ctx.CancellationToken;

        try
        {
            var response = _client.SendAsync(request, requestCt).GetAwaiter().GetResult();
            return StashValue.FromObj(MakeResponse(response));
        }
        catch (HttpRequestException e)
        {
            throw new RuntimeError("http.patch: request failed — " + e.Message, errorType: StashErrorTypes.IOError);
        }
        catch (TaskCanceledException)
        {
            throw new RuntimeError("http.patch: request timed out.", errorType: StashErrorTypes.TimeoutError);
        }
    }

    /// <summary>Downloads the response body of a GET request and writes it to the given file path. Optionally accepts an options dict with 'headers' (dict of name→value pairs) and 'timeout' (int, milliseconds). Returns null.</summary>
    /// <param name="url">The URL to download from</param>
    /// <param name="path">The local file path to write the downloaded content to</param>
    /// <param name="options">An optional dict with 'headers' (dict) and/or 'timeout' (int, milliseconds)</param>
    /// <returns>null</returns>
    [StashFn(ReturnType = "null")]
    private static void Download(IInterpreterContext ctx, string url, string path, params StashValue[] options)
    {
        ValidateUrl(url, "http.download");
        path = ctx.ExpandTilde(path);

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        var timeout = ApplyOptionsValue(request, options.Length > 0 ? options[0] : StashValue.Null, "http.download");
        using var cts = MakeLinkedCts(ctx.CancellationToken, timeout);
        var requestCt = cts?.Token ?? ctx.CancellationToken;

        try
        {
            using var response = _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestCt).GetAwaiter().GetResult();
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
    }

    /// <summary>
    /// Extracts an optional <c>options</c> dict from a single <see cref="StashValue"/>,
    /// applies any <c>headers</c> to the request message, and returns the requested timeout if provided.
    /// </summary>
    private static TimeSpan? ApplyOptionsValue(HttpRequestMessage request, StashValue optionsVal, string funcName)
    {
        if (optionsVal.IsNull) return null;

        if (optionsVal.ToObject() is not StashDictionary options)
            throw new RuntimeError($"{funcName}: options must be a dict.", errorType: StashErrorTypes.TypeError);

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
    /// Creates a <see cref="CancellationTokenSource"/> that combines an outer cancellation token
    /// with an optional per-request timeout. Returns <c>null</c> when neither is active.
    /// </summary>
    private static CancellationTokenSource? MakeLinkedCts(CancellationToken outer, TimeSpan? timeout)
    {
        if (timeout.HasValue && outer.CanBeCanceled)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
            cts.CancelAfter(timeout.Value);
            return cts;
        }
        if (timeout.HasValue)
            return new CancellationTokenSource(timeout.Value);
        if (outer.CanBeCanceled)
            return CancellationTokenSource.CreateLinkedTokenSource(outer);
        return null;
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
