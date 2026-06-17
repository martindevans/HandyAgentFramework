using HandyAgentFramework.SqliteFileStore;
using System.Data;
using System.Data.SQLite;
using System.Reflection;

namespace HandyAgentFramework.Tests
{
    [TestClass]
    public sealed class SqliteFileStoreTests
    {
        public TestContext TestContext { get; set; }

        private string GetLoremIpsum()
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("HandyAgentFramework.Tests.LoremIpsum.txt")!;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        
        [TestMethod]
        public async Task WriteReadFile()
        {
            using var provider = new TestDatabaseProvider();
            var files = new SqliteFileStore.SqliteFileStore("context", provider);

            // Create/write to file
            await files.WriteFileAsync("a/b/c/file.txt", "hello", TestContext.CancellationToken);
            var content = await files.ReadFileAsync("a/b/c/file.txt", TestContext.CancellationToken);
            Assert.AreEqual("hello", content);

            // Overwrite file
            await files.WriteFileAsync("a/b/c/file.txt", "world", TestContext.CancellationToken);
            content = await files.ReadFileAsync("a/b/c/file.txt", TestContext.CancellationToken);
            Assert.AreEqual("world", content);
        }

        [TestMethod]
        public async Task WriteReadRootFile()
        {
            using var provider = new TestDatabaseProvider();
            var files = new SqliteFileStore.SqliteFileStore("context", provider);

            // Create/write to file
            await files.WriteFileAsync("file.txt", "hello", TestContext.CancellationToken);
            var content = await files.ReadFileAsync("file.txt", TestContext.CancellationToken);
            Assert.AreEqual("hello", content);

            // Overwrite file
            await files.WriteFileAsync("file.txt", "world", TestContext.CancellationToken);
            content = await files.ReadFileAsync("file.txt", TestContext.CancellationToken);
            Assert.AreEqual("world", content);
        }

        [TestMethod]
        public async Task DeleteFile()
        {
            using var provider = new TestDatabaseProvider();
            var files = new SqliteFileStore.SqliteFileStore("context", provider);

            // Create/write to file
            await files.WriteFileAsync("a/b/c/file.txt", "hello", TestContext.CancellationToken);
            var content = await files.ReadFileAsync("a/b/c/file.txt", TestContext.CancellationToken);
            Assert.AreEqual("hello", content);

            // Delete file
            var result = await files.DeleteFileAsync("a/b/c/file.txt", TestContext.CancellationToken);
            Assert.IsTrue(result);

            // Check content is null
            content = await files.ReadFileAsync("a/b/c/file.txt", TestContext.CancellationToken);
            Assert.IsNull(content);
        }

        [TestMethod]
        public async Task ListFiles()
        {
            using var provider = new TestDatabaseProvider();
            var files = new SqliteFileStore.SqliteFileStore("context", provider);

            // Create files
            await files.WriteFileAsync("a/b/c/file1.txt", "file1", TestContext.CancellationToken);
            await files.WriteFileAsync("a/b/c/file2.txt", "file2", TestContext.CancellationToken);
            
            // In some other directories
            await files.WriteFileAsync("a/b/d/file3.txt", "file3", TestContext.CancellationToken);
            await files.WriteFileAsync("a/b/file3.txt", "file3", TestContext.CancellationToken);
            await files.WriteFileAsync("a/b/c/d/file3.txt", "file3", TestContext.CancellationToken);

            // Read files in a/b/c
            var listing = await files.ListFilesAsync("a/b/c", TestContext.CancellationToken);

            // Check the items
            Assert.HasCount(2, listing);
            Assert.Contains("file1.txt", listing);
            Assert.Contains("file2.txt", listing);
        }

        [TestMethod]
        public async Task FileExists()
        {
            using var provider = new TestDatabaseProvider();
            var files1 = new SqliteFileStore.SqliteFileStore("context1", provider);
            var files2 = new SqliteFileStore.SqliteFileStore("context2", provider);

            // Create files
            await files1.WriteFileAsync("a/b/c/file1.txt", "file1", TestContext.CancellationToken);

            // Check it exists
            Assert.IsTrue(await files1.FileExistsAsync("a/b/c/file1.txt", TestContext.CancellationToken));
            
            // Check some others do not exist
            Assert.IsFalse(await files1.FileExistsAsync("a/b/c/file2.txt", TestContext.CancellationToken));
            Assert.IsFalse(await files1.FileExistsAsync("a/b/file1.txt", TestContext.CancellationToken));
            Assert.IsFalse(await files1.FileExistsAsync("a/b/c/d/file1.txt", TestContext.CancellationToken));

            // Check for leakage
            Assert.IsFalse(await files2.FileExistsAsync("a/b/c/file1.txt", TestContext.CancellationToken));
        }

        [TestMethod]
        public async Task SearchFilesNoMatchRegex()
        {
            using var provider = new TestDatabaseProvider();
            var files = new SqliteFileStore.SqliteFileStore("context", provider);

            // Create files
            await files.WriteFileAsync("a/b/c/file1.txt", GetLoremIpsum(), TestContext.CancellationToken);

            // Find files with regex
            var result = await files.SearchFilesAsync("a/b/c", "[0-9]+", null, TestContext.CancellationToken);
            Assert.IsEmpty(result);
        }

        [TestMethod]
        public async Task SearchFilesNoMatchGlob()
        {
            using var provider = new TestDatabaseProvider();
            var files = new SqliteFileStore.SqliteFileStore("context", provider);

            // Create files
            await files.WriteFileAsync("a/b/c/file1.txt", GetLoremIpsum(), TestContext.CancellationToken);

            // Find files with regex
            var result = await files.SearchFilesAsync("a/b/c", ".*", "*.md", TestContext.CancellationToken);
            Assert.IsEmpty(result);
        }

        [TestMethod]
        public async Task SearchFilesMatch()
        {
            using var provider = new TestDatabaseProvider();
            var files = new SqliteFileStore.SqliteFileStore("context", provider)
            {
                SnippetLength = 128,
            };

            // Create files
            await files.WriteFileAsync("a/b/c/file1.txt", GetLoremIpsum(), TestContext.CancellationToken);

            // Find files with regex
            var result = await files.SearchFilesAsync("a/b/c", "MarkerB", "**/*.txt", TestContext.CancellationToken);
            Assert.HasCount(1, result);

            const string expected = "...i. In nec dui sollicitudin, cursus sem eu, accumsan lectus. MarkerB. Sed in blandit nulla. Nunc at sapien sed lectus laoreet co...";
            Assert.AreEqual(expected, result.Single().Snippet);
        }

        [TestMethod]
        public async Task SearchFilesMatchNearStart()
        {
            using var provider = new TestDatabaseProvider();
            var files = new SqliteFileStore.SqliteFileStore("context", provider)
            {
                SnippetLength = 128,
            };

            // Create files
            await files.WriteFileAsync("a/b/c/file1.txt", GetLoremIpsum(), TestContext.CancellationToken);

            // Find files with regex
            var result = await files.SearchFilesAsync("a/b/c", "MarkerA", "**/*.txt", TestContext.CancellationToken);
            Assert.HasCount(1, result);

            const string expected = "Lorem ipsum dolor sit amet, consectetur adipiscing elit. MarkerA. In consequat massa placerat, ullamcorper libero eget, dapi...";
            Assert.AreEqual(expected, result.Single().Snippet);
        }

        [TestMethod]
        public async Task SearchFilesMatchNearEnd()
        {
            using var provider = new TestDatabaseProvider();
            var files = new SqliteFileStore.SqliteFileStore("context", provider)
            {
                SnippetLength = 128,
            };

            // Create files
            await files.WriteFileAsync("a/b/c/file1.txt", GetLoremIpsum(), TestContext.CancellationToken);

            // Find files with regex
            var result = await files.SearchFilesAsync("a/b/c", "MarkerC", "**/*.txt", TestContext.CancellationToken);
            Assert.HasCount(1, result);

            const string expected = "...la efficitur purus, a interdum lectus sem fermentum mauris. MarkerC. Aliquam erat volutpat.";
            Assert.AreEqual(expected, result.Single().Snippet);
        }

        [TestMethod]
        public async Task SearchFilesMatchVeryLarge()
        {
            using var provider = new TestDatabaseProvider();
            var files = new SqliteFileStore.SqliteFileStore("context", provider)
            {
                SnippetLength = 128,
            };

            // Create files
            await files.WriteFileAsync("a/b/c/file1.txt", GetLoremIpsum(), TestContext.CancellationToken);

            // Find files with regex
            var result = await files.SearchFilesAsync("a/b/c", "In magna.*semper lacinia", "**/*.txt", TestContext.CancellationToken);
            Assert.HasCount(1, result);

            const string expected = "...la efficitur purus, a interdum lectus sem fermentum mauris. MarkerC. Aliquam erat volutpat.";
            Assert.IsTrue(result.Single().Snippet.StartsWith("...In magna"));
            Assert.IsTrue(result.Single().Snippet.EndsWith("semper lacinia..."));
        }

        [TestMethod]
        public async Task ReadComplexFilePath()
        {
            using var provider = new TestDatabaseProvider();
            var files = new SqliteFileStore.SqliteFileStore("context", provider);

            // Create file
            await files.WriteFileAsync("a/b/c/file.txt", "hello", TestContext.CancellationToken);
            
            // Read with a dot in path (does nothing)
            var content = await files.ReadFileAsync("a/b/./c/file.txt", TestContext.CancellationToken);
            Assert.AreEqual("hello", content);

            // Read with a double dot in path (moves up)
            content = await files.ReadFileAsync("a/b/../b/c/file.txt", TestContext.CancellationToken);
            Assert.AreEqual("hello", content);
        }
    }

    public sealed class TestDatabaseProvider
        : ISqliteFileStoreConnectionProvider, IDisposable
    {
        private readonly IDbConnection _root;
        private readonly string _connStr;

        public TestDatabaseProvider()
        {
            _root = GetConnection();
            _connStr = $"Data Source=file:{Random.Shared.GetHexString(128)}?mode=memory&cache=shared";
        }
        
        public IDbConnection GetConnection()
        {
            return new SQLiteConnection(_connStr);
        }

        public void Dispose()
        {
            _root.Close();
            _root.Dispose();
        }
    }
}
