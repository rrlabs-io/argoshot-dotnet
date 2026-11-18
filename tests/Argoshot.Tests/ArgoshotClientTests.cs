using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Argoshot;
using Xunit;

public class ArgoshotClientTests
{
    // The server's own test (jai-web-framework framework/Screenshots/tests/privacy/app.jai:194):
    // hmac_sha256("secret-of-the-customer-1234", "html=x&access_key=key-<tenant>"), lowercase hex.
    [Fact]
    public void ServerVector()
    {
        var client = new ArgoshotClient("key-2", new ArgoshotClientOptions { SecretKey = "secret-of-the-customer-1234" });
        Assert.Equal(
            "https://api.argoshot.com/take?html=x&access_key=key-2&signature=9488e5f178e81282b921f410775de15b0767f894504cd18bd66f35616d60ffd1",
            client.Url(new TakeOptions { Html = "x" }));
    }

    [Fact]
    public void SignedUrl()
    {
        var client = new ArgoshotClient("AK_test", new ArgoshotClientOptions { SecretKey = "SK_test" });
        var options = new TakeOptions
        {
            Url = "https://example.com/a b?x=1&y=2",
            Format = "png",
            FullPage = true,
            ViewportWidth = 1280,
            ["device_scale_factor"] = 1.5,
            ["hide_selectors"] = new[] { ".a", "#b .c" },
        };
        Assert.Equal(
            "https://api.argoshot.com/take?device_scale_factor=1.5&format=png&full_page=true&hide_selectors=.a&hide_selectors=%23b%20.c&url=https%3A%2F%2Fexample.com%2Fa%20b%3Fx%3D1%26y%3D2&viewport_width=1280&access_key=AK_test&signature=1a6f9d69eae82c57575f031ba76325705f5c0ddc6b48380a6fa7964c8a497b1c",
            client.Url(options));
    }

    [Fact]
    public void NonAsciiHtml()
    {
        var client = new ArgoshotClient("AK_test", new ArgoshotClientOptions { SecretKey = "SK_test" });
        Assert.Equal(
            "https://api.argoshot.com/take?format=png&html=%3Ch1%3EHi%20%E2%80%94%20caf%C3%A9%20%2B%20%26%3C%2Fh1%3E&access_key=AK_test&signature=a1fb4eea53e2c394dce7ab32e31a26a2c8c85b8ebc53ac330ddb0cc441ee9eb7",
            client.Url(new TakeOptions { Html = "<h1>Hi — café + &</h1>", Format = "png" }));
    }

    [Fact]
    public void UnsignedCustomBaseNullSkipped()
    {
        var client = new ArgoshotClient("AK_test", new ArgoshotClientOptions { BaseUrl = "http://localhost:8080/" });
        var options = new TakeOptions { Url = "https://example.com", Format = "png", Selector = null, ["access_key"] = "other", ["signature"] = "x" };
        Assert.Equal("http://localhost:8080/take?format=png&url=https%3A%2F%2Fexample.com&access_key=AK_test", client.Url(options));
    }

    [Fact]
    public void ReservedCharacters()
    {
        Assert.Equal("styles=a%7Bcontent%3A%27%21%28%29%2A%27%7D", ArgoshotClient.BuildQuery(new TakeOptions { ["styles"] = "a{content:'!()*'}" }));
    }

    [Fact]
    public void ParseError()
    {
        var e = ArgoshotClient.ParseError(400, "{\"is_successful\":false,\"error_code\":\"selector_not_found\",\"error_message\":\"Nothing matches the selector: #hero\",\"documentation_url\":\"https://argoshot.com/docs/errors/selector-not-found/\"}");
        Assert.Equal("selector_not_found", e.Code);
        Assert.Equal("Nothing matches the selector: #hero", e.Message);
        Assert.Equal(400, e.Status);
        Assert.Equal("https://argoshot.com/docs/errors/selector-not-found/", e.DocumentationUrl);

        e = ArgoshotClient.ParseError(402, "{\"is_successful\":false,\"error_code\":\"free_tier_exhausted\",\"error_message\":\"Used.\",\"upgrade_url\":\"https://argoshot.com/pricing\"}", "5");
        Assert.Equal("free_tier_exhausted", e.Code);
        Assert.Equal("https://argoshot.com/pricing", e.UpgradeUrl);
        Assert.Equal(5, e.RetryAfter);

        var bad = ArgoshotClient.ParseError(502, "<html>Bad gateway</html>");
        Assert.Equal("http_error", bad.Code);
        Assert.Equal(502, bad.Status);
        Assert.Contains("HTTP 502", bad.Message);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _respond;
        public readonly List<(HttpRequestMessage Request, string? Body, string? ContentType)> Calls = new();

        public FakeHandler(Func<HttpResponseMessage> respond) => _respond = respond;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request, body, request.Content?.Headers.ContentType?.MediaType));
            return _respond();
        }
    }

    [Fact]
    public async Task TakeGetSignedWithHeader()
    {
        var handler = new FakeHandler(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 0x89, 0x50, 0x4E, 0x47 }) });
        var client = new ArgoshotClient("AK_test", new ArgoshotClientOptions { SecretKey = "SK_test", HttpClient = new HttpClient(handler) });
        var bytes = await client.TakeAsync(new TakeOptions { Url = "https://example.com", Format = "png" });
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes);
        var query = "format=png&url=https%3A%2F%2Fexample.com";
        var (request, _, _) = handler.Calls[0];
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://api.argoshot.com/take?" + query + "&signature=" + ArgoshotClient.Sign("SK_test", query), request.RequestUri!.OriginalString);
        Assert.Equal("AK_test", string.Join(",", request.Headers.GetValues("X-Access-Key")));
    }

    [Fact]
    public async Task TakePostJsonForHtml()
    {
        var handler = new FakeHandler(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") });
        var client = new ArgoshotClient("AK_test", new ArgoshotClientOptions { HttpClient = new HttpClient(handler) });
        await client.TakeAsync(new TakeOptions { Html = "<h1>Hi</h1>", Format = "png", FullPage = true, ["hide_selectors"] = new[] { ".x" } });
        var (request, body, contentType) = handler.Calls[0];
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.argoshot.com/take", request.RequestUri!.OriginalString);
        Assert.Equal("application/json", contentType);
        using var doc = JsonDocument.Parse(body!);
        Assert.Equal("<h1>Hi</h1>", doc.RootElement.GetProperty("html").GetString());
        Assert.True(doc.RootElement.GetProperty("full_page").GetBoolean());
        Assert.Equal(".x", doc.RootElement.GetProperty("hide_selectors")[0].GetString());
    }

    [Fact]
    public async Task TakeErrorThrows()
    {
        var handler = new FakeHandler(() => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"is_successful\":false,\"error_code\":\"access_key_invalid\",\"error_message\":\"No such access key.\"}", Encoding.UTF8, "application/json"),
        });
        var client = new ArgoshotClient("bad", new ArgoshotClientOptions { HttpClient = new HttpClient(handler) });
        var e = await Assert.ThrowsAsync<ArgoshotException>(() => client.TakeAsync(new TakeOptions { Url = "https://example.com" }));
        Assert.Equal("access_key_invalid", e.Code);
        Assert.Equal(400, e.Status);
    }

    [Fact]
    public async Task Live()
    {
        var key = Environment.GetEnvironmentVariable("ARGOSHOT_KEY");
        if (string.IsNullOrEmpty(key)) return;   // set ARGOSHOT_KEY to run the live test
        var client = new ArgoshotClient(key, new ArgoshotClientOptions { SecretKey = Environment.GetEnvironmentVariable("ARGOSHOT_SECRET") });
        var bytes = await client.TakeAsync(new TakeOptions { Url = "https://example.com", Format = "png" });
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes[..4]);
    }
}
