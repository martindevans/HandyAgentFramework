using Dapper;
using Microsoft.Agents.AI;
using Microsoft.Extensions.FileSystemGlobbing;
using System.Data;
using System.Text;
using System.Text.RegularExpressions;

namespace HandyAgentFramework.SqliteFileStore;

/// <summary>
/// Provides a connection to an SQLite database for <see cref="SqliteFileStore"/>
/// </summary>
public interface ISqliteFileStoreConnectionProvider
{
    /// <summary>
    /// Get a database connection
    /// </summary>
    /// <returns></returns>
    IDbConnection GetConnection();
}

#pragma warning disable MAAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

/// <summary>
/// Provides an <see cref="AgentFileStore"/> which stores files in an SQLite database
/// </summary>
public sealed class SqliteFileStore
    : AgentFileStore
{
    private readonly string _context;
    private readonly ISqliteFileStoreConnectionProvider _database;

    /// <summary>
    /// Length of snippets returned in file search
    /// </summary>
    public int SnippetLength { get; set; } = 512;

    public SqliteFileStore(string context, ISqliteFileStoreConnectionProvider database)
    {
        _context = context;
        _database = database;
    }

    private static async Task Init(IDbConnection connection, string context)
    {
        // Store directories with a pointer to parent directory
        await connection.ExecuteAsync(
            """
            CREATE TABLE IF NOT EXISTS `AgentFileStoreDirectorys` (
                `Context` TEXT NOT NULL,
                `ID` INTEGER PRIMARY KEY,
                `Name` TEXT NOT NULL,
                `ParentId` INTEGER NOT NULL, -- Root directory refers to self as it's own parent
                UNIQUE (Context, ParentId, Name),
                FOREIGN KEY (ParentId) REFERENCES AgentFileStoreDirectorys(ID)
            );
            """
        );

        // Store files with reference to the directory that owns them
        await connection.ExecuteAsync(
            """
            CREATE TABLE IF NOT EXISTS `AgentFileStoreFiles` (
                `Context` TEXT NOT NULL,
                `Directory` INTEGER, --can be null! represents the root dir
                `FileName` TEXT NOT NULL,
                `Content` TEXT NOT NULL,
                PRIMARY KEY(`Context`, `Directory`, `FileName`),
                FOREIGN KEY(Directory) REFERENCES AgentFileStoreDirectorys(ID)
            );
            """
        );

        // Create root
        await connection.ExecuteAsync(
            """
            INSERT OR IGNORE INTO AgentFileStoreDirectorys (ID, Context, Name, ParentId)
            VALUES (0, @Context, '', 0);
            """,
            new
            {
                Context = context
            }
        );
    }

    /// <summary>
    /// Walk a path from the root locating a directory
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="context"></param>
    /// <param name="path"></param>
    /// <returns></returns>
    private static async Task<long?> GetDirectoryIdAsync(IDbConnection connection, string context, string path)
    {
        // Split path into components
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Start at root directory
        long directoryId = 0;

        // Walk directory hierarchy
        foreach (var part in parts)
        {
            // Find child directory
            var childId = await connection.QuerySingleOrDefaultAsync<long?>(
                """
                SELECT ID
                FROM AgentFileStoreDirectorys
                WHERE Context = @Context
                  AND Name = @Name
                  AND ParentId = @ParentId;
                """,
                new
                {
                    Context = context,
                    Name = part,
                    ParentId = directoryId,
                });

            // Handle missing directory
            if (childId is null)
                return null;

            directoryId = childId.Value;
        }

        // Return resolved directory
        return directoryId;
    }

    public override async Task WriteAsync(string path, string content, CancellationToken cancellationToken = new())
    {
        // Split path
        var (directoryPath, fileName) = SplitPath(path);

        // Get DB
        using var connection = _database.GetConnection();
        await Init(connection, _context);

        // Find directory
        var directoryId = await GetDirectoryIdAsync(connection, _context, directoryPath);
        if (directoryId is null)
            throw new DirectoryNotFoundException($"Directory '{directoryPath}' does not exist.");

        // Store file
        await connection.ExecuteAsync(
            """
            INSERT INTO AgentFileStoreFiles (Context, Directory, FileName, Content)
            VALUES (@Context, @Directory, @FileName, @Content)
            ON CONFLICT(Context, Directory, FileName) DO UPDATE
            SET Content = excluded.Content;
            """,
            new
            {
                Context = _context,
                Directory = directoryId,
                FileName = fileName,
                Content = NormalizeContent(content),
            }
        );
    }

    public override async Task<string?> ReadAsync(string path, CancellationToken cancellationToken = new())
    {
        // Split path
        var (directoryPath, fileName) = SplitPath(path);

        // Get DB
        using var connection = _database.GetConnection();
        await Init(connection, _context);

        // Find directory
        var directoryId = await GetDirectoryIdAsync(connection, _context, directoryPath);
        if (directoryId is null)
            return null;

        // Query file content
        const string query = """
                             SELECT Content
                             FROM AgentFileStoreFiles
                             WHERE Context = @Context
                               AND Directory = @Directory
                               AND FileName = @FileName;
                             """;

        return await connection.QuerySingleOrDefaultAsync<string>(
            query,
            new
            {
                Context = _context,
                Directory = directoryId,
                FileName = fileName,
            });
    }

    public override async Task<bool> DeleteAsync(string path, CancellationToken cancellationToken = new())
    {
        // Split path
        var (directoryPath, fileName) = SplitPath(path);

        // Get DB
        using var connection = _database.GetConnection();
        await Init(connection, _context);

        // Find directory
        var directoryId = await GetDirectoryIdAsync(connection, _context, directoryPath);
        if (directoryId is null)
            return false;

        // Delete file
        const string query = """
                             DELETE FROM AgentFileStoreFiles
                             WHERE Context = @Context
                               AND Directory = @Directory
                               AND FileName = @FileName;
                             """;

        var affectedRows = await connection.ExecuteAsync(
            query,
            new
            {
                Context = _context,
                Directory = directoryId,
                FileName = fileName,
            });

        return affectedRows > 0;
    }

    public override async Task<IReadOnlyList<FileStoreEntry>> ListChildrenAsync(string directory, CancellationToken cancellationToken = new())
    {
        // Normalize the directory path
        var normalizedDirectory = NormalizePath(directory);

        // Get DB connection
        using var connection = _database.GetConnection();
        await Init(connection, _context);

        // Find directory
        var directoryId = await GetDirectoryIdAsync(connection, _context, normalizedDirectory);
        if (directoryId is null)
            return [ ];

        const string directoriesQuery = """
                                        SELECT Name
                                        FROM AgentFileStoreDirectorys
                                        WHERE Context = @Context
                                          AND ((ParentId IS NULL AND @ParentId IS NULL) OR ParentId = @ParentId)
                                        ORDER BY Name;
                                        """;

        const string filesQuery = """
                                  SELECT FileName
                                  FROM AgentFileStoreFiles
                                  WHERE Context = @Context
                                    AND Directory = @Directory
                                  ORDER BY FileName;
                                  """;

        // Get directories
        var directories = await connection.QueryAsync<string>(
            directoriesQuery,
            new
            {
                Context = _context,
                ParentId = directoryId,
            });

        // Get files
        var files = await connection.QueryAsync<string>(
            filesQuery,
            new
            {
                Context = _context,
                Directory = directoryId,
            });

        return
        [
            ..directories.Select(x => new FileStoreEntry(x, FileStoreEntry.Directory)),
            ..files.Select(x => new FileStoreEntry(x, FileStoreEntry.File)),
        ];
    }

    public override async Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = new())
    {
        // Split path
        var (directoryPath, fileName) = SplitPath(path);

        // Get DB
        using var connection = _database.GetConnection();
        await Init(connection, _context);

        // Find directory
        var directoryId = await GetDirectoryIdAsync(connection, _context, directoryPath);
        if (directoryId is null)
            return false;

        // Check if file exists
        const string query = """
                             SELECT COUNT(1)
                             FROM AgentFileStoreFiles
                             WHERE Context = @Context
                               AND Directory = @Directory
                               AND FileName = @FileName;
                             """;

        var count = await connection.ExecuteScalarAsync<int>(
            query,
            new
            {
                Context = _context,
                Directory = directoryId,
                FileName = fileName,
            });

        return count > 0;
    }

    public override async Task<IReadOnlyList<FileSearchResult>> SearchAsync(string directory, string regexPattern, string? globPattern = null, bool recursive = false, CancellationToken cancellationToken = default)
    {
        var normalizedDirectory = NormalizePath(directory);

        // Get the DB
        using var connection = _database.GetConnection();
        await Init(connection, _context);

        // Get the directory we're searching
        var directoryId = await GetDirectoryIdAsync(connection, _context, normalizedDirectory);
        if (normalizedDirectory.Length > 0 && directoryId is null)
            return [];

        // Build regex fir context
        var regex = new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Builder glob matcher
        Matcher? matcher = null;
        if (globPattern != null)
        {
            matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
            matcher.AddInclude(globPattern);
        }

        // Extract results recursively
        var results = new List<FileSearchResult>();
        await SearchDirectory(connection, directoryId, "");
        return results;

        async Task SearchDirectory(IDbConnection connection, long? parentId, string relativePath)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Get files in this directory
            var files = await connection.QueryAsync<AgentFileStoreFile>(
                """
                SELECT Context, Directory, FileName, Content
                FROM AgentFileStoreFiles
                WHERE Context = @Context
                  AND ((Directory IS NULL AND @Directory IS NULL) OR Directory = @Directory)
                """,
                new
                {
                    Context = _context,
                    Directory = parentId,
                });

            // Glob match files
            foreach (var file in files)
            {
                var relativeFile = relativePath + file.FileName;

                if (matcher == null || matcher.Match(relativeFile).HasMatches)
                {
                    var result = SearchFile(file, regex, 200);
                    if (result != null)
                        results.Add(result);
                }
            }

            if (!recursive)
                return;

            // Get child directories
            var directories = await connection.QueryAsync<(long ID, string Name)>(
                """
                SELECT ID, Name
                FROM AgentFileStoreDirectorys
                WHERE Context = @Context
                  AND ((ParentId IS NULL AND @ParentId IS NULL) OR ParentId = @ParentId);
                """,
                new
                {
                    Context = _context,
                    ParentId = parentId,
                });

            // Recursively search children
            foreach (var (id, name) in directories)
                await SearchDirectory(connection, id, relativePath + name + "/");
        }
    }

    public override async Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
    {
        var normalizedPath = NormalizePath(path);
        if (string.IsNullOrEmpty(normalizedPath))
            return;

        using var connection = _database.GetConnection();
        await Init(connection, _context);

        var parts = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        long parentId = 0;

        foreach (var part in parts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var id = await connection.QuerySingleOrDefaultAsync<long?>(
                """
                SELECT ID
                FROM AgentFileStoreDirectorys
                WHERE Context = @Context
                  AND Name = @Name
                  AND (
                        (ParentId IS NULL AND @ParentId IS NULL)
                     OR ParentId = @ParentId
                  );
                """,
                new
                {
                    Context = _context,
                    Name = part,
                    ParentId = parentId,
                }
            );

            id ??= await connection.QuerySingleAsync<long>(
                """
                INSERT INTO AgentFileStoreDirectorys (Context, Name, ParentId)
                VALUES (@Context, @Name, @ParentId)
                RETURNING ID;
                """,
                new
                {
                    Context = _context,
                    Name = part,
                    ParentId = parentId,
                }
            );

            parentId = id.Value;
        }
    }

    #region content normalisation
    private static string NormalizeContent(string content)
    {
        return content.ReplaceLineEndings("\n");
    }
    #endregion

    #region path helpers
    private static IReadOnlyList<string> NormalizePathParts(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return [ ];

        // Normalize separators
        var path = input.Replace('\\', '/');

        // Handle '.' and '..' segments
        var parts = new List<string>();
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (segment)
            {
                case ".":
                    continue;
                case "..":
                {
                    if (parts.Count > 0)
                        parts.RemoveAt(parts.Count - 1);
                    continue;
                }
                default:
                    parts.Add(segment);
                    break;
            }
        }

        return parts;
    }

    private static string NormalizePath(string input)
    {
        return string.Join("/", NormalizePathParts(input));
    }

    private static (string directory, string file) SplitPath(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return ("", "");

        var parts = NormalizePathParts(input);

        // No segments => root
        if (parts.Count == 0)
            return ("", "");

        // Single segment => root file
        if (parts.Count == 1)
            return ("", parts[0]);

        // Split directory/file
        var file = parts[^1];
        var directory = string.Join('/', parts.Take(parts.Count - 1));

        return (directory, file);
    }
    #endregion

    #region search helpers
    private static FileSearchResult? SearchFile(AgentFileStoreFile file, Regex regex, int snippetLength)
    {
        var matches = regex.Matches(file.Content);
        if (matches.Count == 0)
            return null;
            
        // Build index of line starts
        var lineStarts = BuildLineStarts(file.Content);

        // Get all lines that matched
        var lines = new List<FileSearchMatch>();
        foreach (Match match in matches)
        {
            var lineNumber = GetLineNumber(lineStarts, match.Index);
            var lineText = GetLine(file.Content, lineStarts, lineNumber);

            lines.Add(new FileSearchMatch
            {
                Line = lineText,
                LineNumber = lineNumber + 1
            });
        }

        // Return final overall result
        return new FileSearchResult
        {
            FileName = file.FileName,
            Snippet = GetSnippet(file.Content, matches[0].Index, matches[0].Length, snippetLength),
            MatchingLines = lines
        };
    }

    /// <summary>
    /// Build a list of line start indices
    /// </summary>
    /// <param name="text"></param>
    /// <returns></returns>
    private static List<int> BuildLineStarts(string text)
    {
        var lineStarts = new List<int> { 0 };

        for (var i = 0; i < text.Length; i++)
            if (text[i] == '\n')
                lineStarts.Add(i + 1);

        return lineStarts;
    }

    /// <summary>
    /// Given a list of line start indices and a character index, get the line number of that character
    /// </summary>
    /// <param name="lineStarts"></param>
    /// <param name="charIndex"></param>
    /// <returns></returns>
    private static int GetLineNumber(List<int> lineStarts, int charIndex)
    { 
        var search = lineStarts.BinarySearch(charIndex);
        if (search >= 0)
            return search + 1;
        return ~search;
    }

    /// <summary>
    /// Get the text for a line
    /// </summary>
    /// <param name="text"></param>
    /// <param name="lineStarts"></param>
    /// <param name="lineNumber"></param>
    /// <returns></returns>
    private static string GetLine(string text, List<int> lineStarts, int lineNumber)
    {
        var start = lineStarts[lineNumber - 1];

        var end = lineNumber < lineStarts.Count
            ? lineStarts[lineNumber] - 1
            : text.Length;

        return new string(text.AsSpan()[start..end].TrimEnd('\n').TrimEnd('\n'));
    }

    /// <summary>
    /// Try to get a snippet around a given match
    /// </summary>
    /// <param name="text"></param>
    /// <param name="matchLength"></param>
    /// <param name="maxChars"></param>
    /// <param name="matchIndex"></param>
    /// <returns></returns>
    private static string GetSnippet(string text, int matchIndex, int matchLength, int maxChars)
    {
        // Get the actual matched content
        var match = text.AsSpan(matchIndex, matchLength);
        match = match.Trim();

        // Get prefix and suffix spans
        maxChars -= match.Length;
        if (maxChars < 0)
        {
            var builder = new StringBuilder();
            if (matchIndex != 0)
                builder.Append("...");
            builder.Append(match);
            if (matchIndex + matchLength != text.Length)
                builder.Append("...");
            return builder.ToString();
        }
        else
        {
            // Get prefix and suffix
            var extraLength = maxChars / 2;
            var prefix = SafeSlice(text, matchIndex - extraLength, extraLength, out var hitStart, out _).TrimStart();
            var suffix = SafeSlice(text, matchIndex + matchLength, extraLength, out _, out var hitEnd).TrimEnd();

            // Build final result
            var builder = new StringBuilder();
            if (!hitStart)
                builder.Append("...");
            builder.Append(prefix);
            builder.Append(match);
            builder.Append(suffix);
            if (!hitEnd)
                builder.Append("...");
            return builder.ToString();
        }

        static ReadOnlySpan<char> SafeSlice(string content, int start, int length, out bool hitStart, out bool hitEnd)
        {
            hitStart = false;
            hitEnd = false;

            if (string.IsNullOrEmpty(content))
                return ReadOnlySpan<char>.Empty;

            if (start < 0)
            {
                length += start;
                start = 0;
                hitStart = true;
            }

            if (start >= content.Length || length <= 0)
            {
                hitStart |= start >= content.Length;
                return ReadOnlySpan<char>.Empty;
            }

            if (start + length > content.Length)
            {
                length = content.Length - start;
                hitEnd = true;
            }

            return content.AsSpan(start, length);
        }
    }
    #endregion

    private record AgentFileStoreFile(string Context, long Directory, string FileName, string Content);
    private record AgentFileStoreDirectory(string Context, long ID, string Name, string? Parent);
}