namespace Linkbelli.Application.Export;

/// <summary>What a user's data can be handed back to them as.</summary>
public enum ExportFormat
{
    /// <summary>Everything, structured: playlists, items, folders and sources.</summary>
    Json,

    /// <summary>One row per link — the shape the CSV importer reads back.</summary>
    Csv,

    /// <summary>Netscape bookmark file: importable by every browser.</summary>
    Html,

    /// <summary>OPML subscription list of the caller's feed sources.</summary>
    Opml,
}

public static class ExportFormats
{
    /// <summary>Maps a requested format name to a value; null when unrecognised.</summary>
    public static ExportFormat? Parse(string? format) => format?.Trim().ToLowerInvariant() switch
    {
        null or "" or "json" => ExportFormat.Json,
        "csv" => ExportFormat.Csv,
        "html" or "bookmarks" => ExportFormat.Html,
        "opml" => ExportFormat.Opml,
        _ => null,
    };

    public static string ContentType(ExportFormat format) => format switch
    {
        ExportFormat.Json => "application/json; charset=utf-8",
        ExportFormat.Csv => "text/csv; charset=utf-8",
        ExportFormat.Html => "text/html; charset=utf-8",
        _ => "text/x-opml; charset=utf-8",
    };

    public static string FileExtension(ExportFormat format) => format switch
    {
        ExportFormat.Json => "json",
        ExportFormat.Csv => "csv",
        ExportFormat.Html => "html",
        _ => "opml",
    };
}
