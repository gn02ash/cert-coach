using System.Net.Http.Headers;
using System.Text;
using CertCoach.Application.Certifications.Outlines.Import;
using CertCoach.Application.Documents.Ingestion;

namespace CertCoach.Infrastructure.Web;

/// <summary>
/// Fetches HTML pages and PDFs for outline import, through <see cref="PublicNetwork"/> so private addresses are
/// never reached. PDFs are read with the same extractor as uploads.
/// </summary>
internal sealed class HttpWebPageFetcher(HttpClient http, IPdfTextExtractor pdf) : IWebPageFetcher
{
    public const int MaxBytes = 5 * 1024 * 1024;
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public async Task<WebPage> FetchAsync(Uri url, CancellationToken ct)
    {
        // Covers reading the body too, which HttpClient.Timeout doesn't once the headers are in.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            return await FetchCoreAsync(url, timeout.Token);
        }
        catch (OperationCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new PageFetchException($"{url.Host} didn't answer within {Timeout.TotalSeconds:0} seconds.", e);
        }
    }

    private async Task<WebPage> FetchCoreAsync(Uri url, CancellationToken ct)
    {
        if (url.Scheme is not ("http" or "https"))
        {
            throw new PageFetchException("Only http and https addresses can be imported.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/pdf;q=0.9,text/plain;q=0.8");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("CertCoach", "1.0"));

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException e) when (e.InnerException is PrivateAddressException)
        {
            throw new PageFetchException($"{url.Host} is on a private network, which Cert Coach doesn't fetch from.", e);
        }
        catch (HttpRequestException e)
        {
            throw new PageFetchException($"Couldn't reach {url.Host}.", e);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new PageFetchException($"{url} returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            var finalUrl = response.RequestMessage?.RequestUri ?? url;
            var bytes = await ReadLimitedAsync(response.Content, ct);
            var mediaType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();

            if (mediaType == "application/pdf" || bytes.AsSpan().StartsWith("%PDF-"u8))
            {
                return FromPdf(bytes, finalUrl);
            }

            if (mediaType is not (null or "text/html" or "application/xhtml+xml" or "text/plain"))
            {
                throw new PageFetchException($"{finalUrl} is a {mediaType} file. Import a web page or a PDF.");
            }

            var text = Decode(bytes, response.Content.Headers.ContentType?.CharSet);
            return mediaType == "text/plain"
                ? new WebPage(finalUrl, null, text, [])
                : HtmlText.Convert(text, finalUrl);
        }
    }

    private WebPage FromPdf(byte[] bytes, Uri url)
    {
        ExtractedDocument document;
        try
        {
            document = pdf.Extract(new MemoryStream(bytes));
        }
        catch (InvalidDocumentException e)
        {
            throw new PageFetchException($"{url} isn't a readable PDF.", e);
        }

        if (!document.HasText)
        {
            throw new PageFetchException($"{url} is a scanned PDF with no text to read.");
        }

        var text = string.Join('\n', document.Blocks.Select(b =>
            b.Kind == BlockKind.Heading ? new string('#', b.HeadingLevel) + " " + b.Text : b.Text));
        return new WebPage(url, document.Title, text, []);
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, CancellationToken ct)
    {
        if (content.Headers.ContentLength > MaxBytes)
        {
            throw new PageFetchException($"The page is larger than {MaxBytes / 1024 / 1024} MB.");
        }

        await using var stream = await content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[81_920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
            {
                throw new PageFetchException($"The page is larger than {MaxBytes / 1024 / 1024} MB.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static string Decode(byte[] bytes, string? charset)
    {
        try
        {
            return (charset is null ? Encoding.UTF8 : Encoding.GetEncoding(charset.Trim('"'))).GetString(bytes);
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
