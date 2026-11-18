# Argoshot

The official .NET client for [Argoshot](https://argoshot.com), the screenshot API: screenshots, PDFs and signed
`og:image` URLs of any web page or HTML. **Free to start.** Docs: https://argoshot.com/docs

.NET Standard 2.0 and .NET 8. No dependencies beyond `System.Text.Json` (built in on .NET 8).

## Install

```sh
dotnet add package Argoshot
```

Get your access key (and secret key, for signed URLs) from the dashboard's API keys page.

## Screenshot to a file

```csharp
using Argoshot;

var client = new ArgoshotClient(Environment.GetEnvironmentVariable("ARGOSHOT_ACCESS_KEY")!);

byte[] png = await client.TakeAsync(new TakeOptions
{
    Url = "https://example.com",
    Format = "png",
    FullPage = true,
    BlockCookieBanners = true,
});
await File.WriteAllBytesAsync("example.png", png);
```

## Signed og:image URL

`Url()` builds a `GET /take` link for `<img src>` or `<meta property="og:image">`. With a secret key it is signed
(HMAC-SHA256 of the query string), so nobody can change its options. Set the key to **signed requests only** in the
dashboard before you put its links in public pages.

```csharp
var client = new ArgoshotClient(
    Environment.GetEnvironmentVariable("ARGOSHOT_ACCESS_KEY")!,
    new ArgoshotClientOptions { SecretKey = Environment.GetEnvironmentVariable("ARGOSHOT_SECRET_KEY") });

string ogImage = client.Url(new TakeOptions
{
    Url = "https://example.com/blog/my-post",
    ViewportWidth = 1200,
    ViewportHeight = 630,
    Format = "png",
    Cache = true,
    CacheTtl = 86400,
});
// <meta property="og:image" content="@ogImage">
```

## HTML to PNG

When `Html` (or `Markdown`) is given, `TakeAsync` sends a JSON `POST` instead of a long query string.

```csharp
byte[] card = await client.TakeAsync(new TakeOptions
{
    Html = "<div style=\"font: 64px system-ui; padding: 80px\">Hello from Argoshot</div>",
    Format = "png",
    ViewportWidth = 1200,
    ViewportHeight = 630,
});
await File.WriteAllBytesAsync("card.png", card);
```

## Options

`TakeOptions` has typed properties for the common options (`Url`, `Html`, `Markdown`, `Format`, `ViewportWidth`,
`ViewportHeight`, `ViewportDevice`, `FullPage`, `BlockCookieBanners`, `BlockAds`, `DarkMode`, `Selector`, `Cache`,
`CacheTtl`); any other option of https://argoshot.com/docs/options is set by its API name:
`options["hide_selectors"] = new[] { ".banner", "#chat" }` or `options.Set("delay", 2)`.

## Errors

A failed request throws `ArgoshotException` with the API's `Code` (e.g. `selector_not_found`,
`screenshots_limit_reached`), `Message`, HTTP `Status`, `DocumentationUrl`, and `UpgradeUrl` / `RetryAfter` when the API
gives them. All codes: https://argoshot.com/docs/errors

```csharp
try
{
    await client.TakeAsync(new TakeOptions { Url = "https://example.com", Selector = "#missing", ["error_on_selector_not_found"] = true });
}
catch (ArgoshotException e)
{
    Console.Error.WriteLine($"{e.Code}: {e.Message}");
}
```

## Usage

```csharp
Usage usage = await client.UsageAsync(); // usage.Available, usage.Used, usage.Total ...
```

## Configuration

```csharp
new ArgoshotClient(accessKey, new ArgoshotClientOptions
{
    SecretKey = "...",                      // sign Url() and GET TakeAsync() requests
    BaseUrl = "https://api.argoshot.com",   // default
    HttpClient = httpClient,                // default: a new HttpClient with a 120 s timeout
});
```

Note: a key set to *signed requests only* cannot send JSON `POST` requests (the API refuses them), so `TakeAsync` with
`Html` needs a key that permits unsigned requests.

## Licence

MIT
