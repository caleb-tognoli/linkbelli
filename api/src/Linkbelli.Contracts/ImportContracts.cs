namespace Linkbelli.Contracts;

public record ImportRow(string Url, string? Note);

public record ImportRequest(ImportRow[] Rows, Guid? PlaylistId, string? NewPlaylistName);

public record ImportResult(int Imported, int Skipped, List<string> Errors);
