using Microsoft.Agents.AI;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;

namespace HandyAgentFramework.SqliteFileStore
{
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
    public class SqliteFileStore
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

        private static async Task Init(IDbConnection connection)
        {
            await connection.ExecuteAsync(
                """
                CREATE TABLE IF NOT EXISTS `AgentFileStoreFiles` (
                    `Context` TEXT NOT NULL,
                    `DirectoryPath` TEXT NOT NULL,
                    `FileName` TEXT NOT NULL,
                    `Content` TEXT NOT NULL,
                    PRIMARY KEY(`Context`, `DirectoryPath`, `FileName`)
                );
                """
            );
        }

        public override async Task WriteFileAsync(string path, string content, CancellationToken cancellationToken = new())
        {
            // Split path
            var (directoryPath, fileName) = SplitPath(path);

            // Get DB
            using var connection = _database.GetConnection();
            await Init(connection);

            // Store file
            var fileRecord = new AgentFileStoreFile(_context, directoryPath, fileName, NormalizeContent(content));
            await connection.ExecuteAsync(
                """
                INSERT INTO `AgentFileStoreFiles` (`Context`, `DirectoryPath`, `FileName`, `Content`)
                VALUES (@Context, @DirectoryPath, @FileName, @Content)
                ON CONFLICT(`Context`, `DirectoryPath`, `FileName`) DO UPDATE SET `Content` = excluded.`Content`;
                """,
                fileRecord
            );
        }

        public override async Task<string?> ReadFileAsync(string path, CancellationToken cancellationToken = new())
        {
            // Split path
            var (directoryPath, fileName) = SplitPath(path);

            // Get DB
            using var connection = _database.GetConnection();
            await Init(connection);

            // Query file content
            const string query = """
                                 SELECT `Content`
                                 FROM `AgentFileStoreFiles`
                                 WHERE `Context` = @Context AND `DirectoryPath` = @DirectoryPath AND `FileName` = @FileName;
                                 """;

            return await connection.QuerySingleOrDefaultAsync<string>(query, new { Context = _context, DirectoryPath = directoryPath, FileName = fileName });
        }

        public override async Task<bool> DeleteFileAsync(string path, CancellationToken cancellationToken = new())
        {
            // Split path
            var (directoryPath, fileName) = SplitPath(path);

            // Get DB
            using var connection = _database.GetConnection();
            await Init(connection);

            // Delete file
            const string query = """
                                 DELETE FROM `AgentFileStoreFiles`
                                 WHERE `Context` = @Context AND `DirectoryPath` = @DirectoryPath AND `FileName` = @FileName;
                                 """;
            var affectedRows = await connection.ExecuteAsync(query, new { Context = _context, DirectoryPath = directoryPath, FileName = fileName });
            
            // Check if anything was deleted
            return affectedRows > 0;
        }

        public override async Task<IReadOnlyList<string>> ListFilesAsync(string directory, CancellationToken cancellationToken = new())
        {
            // Normalize the directory path
            var normalizedDirectory = NormalizePath(directory);

            // Get DB connection
            using var connection = _database.GetConnection();
            await Init(connection);

            // Query to list files in the specified directory
            const string query = """
                                 SELECT `FileName`
                                 FROM `AgentFileStoreFiles`
                                 WHERE `Context` = @Context AND `DirectoryPath` = @DirectoryPath;
                                 """;

            var fileNames = await connection.QueryAsync<string>(query, new { Context = _context, DirectoryPath = normalizedDirectory });

            return fileNames.ToList();
        }

        public override async Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = new())
        {
            // Split path
            var (directoryPath, fileName) = SplitPath(path);

            // Get DB
            using var connection = _database.GetConnection();
            await Init(connection);

            // Check if file exists
            const string query = """
                                 SELECT COUNT(1)
                                 FROM `AgentFileStoreFiles`
                                 WHERE `Context` = @Context AND `DirectoryPath` = @DirectoryPath AND `FileName` = @FileName;
                                 """;

            var count = await connection.ExecuteScalarAsync<int>(query, new { Context = _context, DirectoryPath = directoryPath, FileName = fileName });
            return count > 0;
        }

        public override async Task<IReadOnlyList<FileSearchResult>> SearchFilesAsync(string directory, string regexPattern, string? filePattern = null, CancellationToken cancellationToken = new())
        {
            // Normalize the directory path
            var normalizedDirectory = NormalizePath(directory);

            // Get DB connection
            using var connection = _database.GetConnection();
            await Init(connection);

            // Build the query
            const string query = """
                                 SELECT *
                                 FROM `AgentFileStoreFiles`
                                 WHERE `Context` = @Context
                                 AND `DirectoryPath` = @DirectoryPath
                                 AND (@Glob IS NULL OR concat(`DirectoryPath`,'/',`FileName`) GLOB @Glob)
                                 """;

            // Get the files in the directory, filtered by regex match on content
            var files = (await connection.QueryAsync<AgentFileStoreFile>(query, new
            {
                Context = _context,
                DirectoryPath = normalizedDirectory,
                Glob = filePattern
            })).ToList();

            // Filter files
            var regex = new Regex(regexPattern);
            var results = new List<FileSearchResult>();
            foreach (var file in files)
            {
                var r = SearchFile(file, regex, SnippetLength);
                if (r != null)
                    results.Add(r);
            }

            return results;
        }

        [ExcludeFromCodeCoverage]
        public override Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = new CancellationToken())
        {
            // We store the path each file is in, so a directory does not actually need creating!
            return Task.CompletedTask;
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

        private record AgentFileStoreFile(string Context, string DirectoryPath, string FileName, string Content);
    }
}
