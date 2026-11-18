using System;
using System.Collections;
using System.Collections.Generic;

namespace Argoshot
{
    /// <summary>
    /// A request's options, by the API's own names (https://argoshot.com/docs/options). The common ones are typed
    /// properties; any other is set by name: <c>options["hide_selectors"] = new[] { ".banner", "#chat" }</c> or
    /// <c>options.Set("delay", 2)</c>. Null values are left out.
    /// </summary>
    public sealed class TakeOptions : IEnumerable<KeyValuePair<string, object?>>
    {
        private readonly SortedDictionary<string, object?> _values = new SortedDictionary<string, object?>(StringComparer.Ordinal);

        /// <summary>Any option by its API name.</summary>
        public object? this[string name]
        {
            get => _values.TryGetValue(name, out var v) ? v : null;
            set => _values[name] = value;
        }

        /// <summary>Sets any option by its API name; returns this, to chain.</summary>
        public TakeOptions Set(string name, object? value)
        {
            _values[name] = value;
            return this;
        }

        /// <summary>For collection initializers: <c>new TakeOptions { { "delay", 2 } }</c>.</summary>
        public void Add(string name, object? value) => _values[name] = value;

        /// <summary>The page to render (http or https). One of url, html or markdown.</summary>
        public string? Url { get => Get<string>("url"); set => this["url"] = value; }

        /// <summary>HTML to render (TakeAsync sends it as a JSON POST).</summary>
        public string? Html { get => Get<string>("html"); set => this["html"] = value; }

        /// <summary>Markdown to render, as a page (TakeAsync sends it as a JSON POST).</summary>
        public string? Markdown { get => Get<string>("markdown"); set => this["markdown"] = value; }

        /// <summary>png, jpeg (the default jpg), webp, gif, avif, pdf, html, markdown ...</summary>
        public string? Format { get => Get<string>("format"); set => this["format"] = value; }

        /// <summary>Default 1280.</summary>
        public int? ViewportWidth { get => GetValue<int>("viewport_width"); set => this["viewport_width"] = value; }

        /// <summary>Default 1024.</summary>
        public int? ViewportHeight { get => GetValue<int>("viewport_height"); set => this["viewport_height"] = value; }

        /// <summary>A device preset (GET /devices lists them).</summary>
        public string? ViewportDevice { get => Get<string>("viewport_device"); set => this["viewport_device"] = value; }

        public bool? FullPage { get => GetValue<bool>("full_page"); set => this["full_page"] = value; }

        public bool? BlockCookieBanners { get => GetValue<bool>("block_cookie_banners"); set => this["block_cookie_banners"] = value; }

        public bool? BlockAds { get => GetValue<bool>("block_ads"); set => this["block_ads"] = value; }

        public bool? DarkMode { get => GetValue<bool>("dark_mode"); set => this["dark_mode"] = value; }

        /// <summary>Capture one element (CSS selector).</summary>
        public string? Selector { get => Get<string>("selector"); set => this["selector"] = value; }

        /// <summary>Keep the result and answer identical requests from it.</summary>
        public bool? Cache { get => GetValue<bool>("cache"); set => this["cache"] = value; }

        /// <summary>Seconds the cached result is kept (needs Cache).</summary>
        public int? CacheTtl { get => GetValue<int>("cache_ttl"); set => this["cache_ttl"] = value; }

        /// <summary>The options a request sends, sorted by name: nulls and the client's own names (access_key, signature) left out.</summary>
        internal IEnumerable<KeyValuePair<string, object>> Cleaned()
        {
            foreach (var pair in _values)
            {
                if (pair.Value is null || pair.Key == "access_key" || pair.Key == "signature") continue;
                yield return new KeyValuePair<string, object>(pair.Key, pair.Value);
            }
        }

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _values.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private T? Get<T>(string name) where T : class => this[name] as T;

        private T? GetValue<T>(string name) where T : struct => this[name] is T v ? v : (T?)null;
    }
}
