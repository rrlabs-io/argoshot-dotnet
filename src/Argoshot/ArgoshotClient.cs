using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Argoshot
{
    /// <summary>Settings of an <see cref="ArgoshotClient"/>.</summary>
    public sealed class ArgoshotClientOptions
    {
        /// <summary>Your secret key: Url() links and GET TakeAsync requests are then signed.</summary>
        public string? SecretKey { get; set; }

        /// <summary>Default https://api.argoshot.com</summary>
        public string BaseUrl { get; set; } = ArgoshotClient.DefaultBaseUrl;

        /// <summary>The HttpClient requests use (default: a new one with a 120 second timeout).</summary>
        public HttpClient? HttpClient { get; set; }
    }

    /// <summary>GET /usage's answer.</summary>
    public sealed class Usage
    {
        [JsonPropertyName("total")] public int Total { get; set; }
        [JsonPropertyName("available")] public int Available { get; set; }
        [JsonPropertyName("used")] public int Used { get; set; }
        [JsonPropertyName("lifetime")] public bool Lifetime { get; set; }
        [JsonPropertyName("concurrency")] public UsageConcurrency? Concurrency { get; set; }
        [JsonPropertyName("rate")] public UsageRate? Rate { get; set; }
        [JsonPropertyName("period_end")] public long PeriodEnd { get; set; }
    }

    public sealed class UsageConcurrency { [JsonPropertyName("limit")] public int Limit { get; set; } }

    public sealed class UsageRate { [JsonPropertyName("per_minute")] public int PerMinute { get; set; } }

    /// <summary>
    /// A client for the Argoshot screenshot API (https://argoshot.com/docs).
    /// <code>
    /// var client = new ArgoshotClient("ACCESS_KEY");
    /// byte[] png = await client.TakeAsync(new TakeOptions { Url = "https://example.com", Format = "png" });
    /// </code>
    /// </summary>
    public sealed class ArgoshotClient
    {
        public const string Version = "0.1.0";
        public const string DefaultBaseUrl = "https://api.argoshot.com";

        private readonly string _accessKey;
        private readonly string? _secretKey;
        private readonly string _baseUrl;
        private readonly HttpClient _http;

        public ArgoshotClient(string accessKey, ArgoshotClientOptions? options = null)
        {
            if (string.IsNullOrEmpty(accessKey)) throw new ArgumentException("Argoshot: an access key is required", nameof(accessKey));
            options ??= new ArgoshotClientOptions();
            _accessKey = accessKey;
            _secretKey = string.IsNullOrEmpty(options.SecretKey) ? null : options.SecretKey;
            _baseUrl = (string.IsNullOrEmpty(options.BaseUrl) ? DefaultBaseUrl : options.BaseUrl).TrimEnd('/');
            _http = options.HttpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        }

        /// <summary>
        /// A GET /take URL for these options (for &lt;img src&gt; or og:image), signed when a secret key is set.
        /// The access key is in the URL: for public pages, use a key set to "signed requests only".
        /// </summary>
        public string Url(TakeOptions options)
        {
            var query = BuildQuery(options);
            query += (query.Length == 0 ? "" : "&") + "access_key=" + Encode(_accessKey);
            if (_secretKey != null) query += "&signature=" + Sign(_secretKey, query);
            return _baseUrl + "/take?" + query;
        }

        /// <summary>
        /// Renders and returns the file's bytes. GET for url captures (signed when a secret key is set); a JSON POST
        /// when html or markdown is given. Throws <see cref="ArgoshotException"/> when the API answers an error.
        /// </summary>
        public async Task<byte[]> TakeAsync(TakeOptions options, CancellationToken cancellationToken = default)
        {
            var opts = options.Cleaned().ToList();
            HttpRequestMessage request;
            if (opts.Any(p => p.Key == "html" || p.Key == "markdown"))
            {
                var body = JsonSerializer.Serialize(opts.ToDictionary(p => p.Key, p => p.Value));
                request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/take")
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                };
            }
            else
            {
                var query = BuildQuery(options);
                if (_secretKey != null) query += "&signature=" + Sign(_secretKey, query);
                request = new HttpRequestMessage(HttpMethod.Get, _baseUrl + "/take?" + query);
            }
            return await SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>GET /usage: renders total, available and used, and the plan's limits. Costs nothing.</summary>
        public async Task<Usage> UsageAsync(CancellationToken cancellationToken = default)
        {
            var body = await SendAsync(new HttpRequestMessage(HttpMethod.Get, _baseUrl + "/usage"), cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<Usage>(body) ?? new Usage();
        }

        private async Task<byte[]> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using (request)
            {
                request.Headers.TryAddWithoutValidation("X-Access-Key", _accessKey);
                request.Headers.TryAddWithoutValidation("User-Agent", "argoshot-dotnet/" + Version);
                using (var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    var status = (int)response.StatusCode;
                    if (status < 200 || status > 299)
                    {
                        string? retryAfter = null;
                        if (response.Headers.RetryAfter?.Delta is TimeSpan delta) retryAfter = ((int)delta.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                        throw ParseError(status, Encoding.UTF8.GetString(bytes), retryAfter);
                    }
                    return bytes;
                }
            }
        }

        /// <summary>The query string of these options: names sorted, lists repeated, nulls left out (no access key).</summary>
        public static string BuildQuery(TakeOptions options)
        {
            var parts = new List<string>();
            foreach (var pair in options.Cleaned())
            {
                foreach (var value in Values(pair.Value))
                {
                    parts.Add(Encode(pair.Key) + "=" + Encode(Text(value)));
                }
            }
            return string.Join("&", parts);
        }

        /// <summary>The signature the API checks: lowercase hex HMAC-SHA256 of the query string, under the secret key.</summary>
        public static string Sign(string secretKey, string query)
        {
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey)))
            {
                var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(query));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        /// <summary>An <see cref="ArgoshotException"/> from an error response's status, body and Retry-After header.</summary>
        public static ArgoshotException ParseError(int status, string body, string? retryAfter = null)
        {
            int? retry = int.TryParse(retryAfter?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var r) ? r : (int?)null;
            try
            {
                using (var doc = JsonDocument.Parse(body))
                {
                    var root = doc.RootElement;
                    if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error_code", out var code) && code.ValueKind == JsonValueKind.String)
                    {
                        string? Str(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                        int? returned = root.TryGetProperty("returned_status_code", out var rs) && rs.ValueKind == JsonValueKind.Number ? rs.GetInt32() : (int?)null;
                        var errorCode = code.GetString() ?? "http_error";
                        return new ArgoshotException(errorCode, Str("error_message") ?? errorCode, status, Str("documentation_url"), Str("upgrade_url"),
                            returned, Str("matched_string"), retry, body);
                    }
                }
            }
            catch (JsonException)
            {
            }
            var snippet = body.Length > 200 ? body.Substring(0, 200) : body;
            return new ArgoshotException("http_error", "HTTP " + status + (snippet.Length > 0 ? ": " + snippet : ""), status, retryAfter: retry, body: body);
        }

        private static IEnumerable<object> Values(object value)
        {
            if (value is string || !(value is IEnumerable list))
            {
                yield return value;
                yield break;
            }
            foreach (var item in list)
            {
                if (item != null) yield return item;
            }
        }

        private static string Text(object value)
        {
            switch (value)
            {
                case string s: return s;
                case bool b: return b ? "true" : "false";
                case double d: return d.ToString("R", CultureInfo.InvariantCulture);
                case float f: return f.ToString("R", CultureInfo.InvariantCulture);
                case IFormattable formattable: return formattable.ToString(null, CultureInfo.InvariantCulture);
                default: return value.ToString() ?? "";
            }
        }

        /// <summary>RFC 3986: everything but A-Z a-z 0-9 - . _ ~ is percent-encoded (a space is %20, a + is %2B).</summary>
        private static string Encode(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var b in Encoding.UTF8.GetBytes(s))
            {
                var c = (char)b;
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '.' || c == '_' || c == '~') sb.Append(c);
                else sb.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }
    }
}
