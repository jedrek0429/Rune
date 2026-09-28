using System.Text;

using NetCord;

using Rune.Core.Runes;

namespace Rune.Bot;

public sealed class RuneUploadReader(
    IHttpClientFactory httpClientFactory)
{
    private const int MaxSourceSize =
        64 * 1024;

    public async ValueTask<RuneUpload> ReadAsync(
        Attachment file,
        CancellationToken cancellationToken = default)
    {
        if (file.Size > MaxSourceSize)
        {
            return RuneUpload.Fail(
                "Rune source may not exceed 64 KiB.");
        }

        var language =
            GetLanguage(file.FileName);

        if (language is null)
        {
            return RuneUpload.Fail(
                "Supported files are .js, .mjs, .ts, .py, .rs, .c, .cc, .cpp, and .cxx.");
        }

        try
        {
            var client =
                httpClientFactory.CreateClient();

            using var response =
                await client.GetAsync(
                    file.Url,
                    cancellationToken);

            response.EnsureSuccessStatusCode();

            var bytes =
                await response.Content
                    .ReadAsByteArrayAsync(
                        cancellationToken);

            if (bytes.Length > MaxSourceSize)
            {
                return RuneUpload.Fail(
                    "Rune source may not exceed 64 KiB.");
            }

            return new RuneUpload(
                language,
                Encoding.UTF8.GetString(bytes),
                null);
        }
        catch (HttpRequestException)
        {
            return RuneUpload.Fail(
                "The uploaded file could not be downloaded.");
        }
    }

    private static RuneLanguage? GetLanguage(
        string fileName) =>
        Path.GetExtension(fileName)
            .ToLowerInvariant()
            switch
            {
                ".js" or ".mjs" =>
                    RuneLanguage.JavaScript,
                ".ts" =>
                    RuneLanguage.TypeScript,
                ".py" =>
                    RuneLanguage.Python,
                ".rs" =>
                    RuneLanguage.Rust,
                ".c" =>
                    RuneLanguage.C,
                ".cc" or ".cpp" or ".cxx" =>
                    RuneLanguage.Cpp,
                _ => null
            };
}

public sealed record RuneUpload(
    RuneLanguage? Language,
    string? Source,
    string? Error)
{
    public static RuneUpload Fail(
        string error) =>
        new(
            null,
            null,
            error);
}
