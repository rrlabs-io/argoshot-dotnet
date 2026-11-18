using System;

namespace Argoshot
{
    /// <summary>
    /// An error the API answered (its error_code and error_message), or a response that was not the API's
    /// (code "http_error").
    /// </summary>
    public sealed class ArgoshotException : Exception
    {
        public ArgoshotException(string code, string message, int status, string? documentationUrl = null, string? upgradeUrl = null,
            int? returnedStatusCode = null, string? matchedString = null, int? retryAfter = null, string? body = null)
            : base(message)
        {
            Code = code;
            Status = status;
            DocumentationUrl = documentationUrl;
            UpgradeUrl = upgradeUrl;
            ReturnedStatusCode = returnedStatusCode;
            MatchedString = matchedString;
            RetryAfter = retryAfter;
            Body = body;
        }

        /// <summary>The API's error_code, e.g. "selector_not_found".</summary>
        public string Code { get; }

        /// <summary>The HTTP status.</summary>
        public int Status { get; }

        public string? DocumentationUrl { get; }

        public string? UpgradeUrl { get; }

        public int? ReturnedStatusCode { get; }

        public string? MatchedString { get; }

        /// <summary>Seconds, from Retry-After.</summary>
        public int? RetryAfter { get; }

        /// <summary>The raw response body.</summary>
        public string? Body { get; }
    }
}
