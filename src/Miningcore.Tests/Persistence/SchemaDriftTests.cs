using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Miningcore.Tests.Persistence;

/// <summary>
/// Guards against the repositories writing columns that createdb.sql does not create.
///
/// Why this exists: in Oct 2026 the fork added blocks.difficulty and
/// auxblocks.difficulty / auxblocks.networkdifficulty to the entities and INSERT statements
/// without a schema change. On a database created from the shipped scripts every block insert
/// failed with "column does not exist", and because the block insert shares a transaction with
/// the share batch, whole share batches went to the recovery file. Production only worked because
/// the columns had been added by hand. This test turns that class of drift into a build failure.
/// </summary>
public class SchemaDriftTests
{
    private const string CreateDbScript = "src/Miningcore/Persistence/Postgres/Scripts/createdb.sql";
    private const string RepositoryDir = "src/Miningcore/Persistence/Postgres/Repositories";

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while(dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "Miningcore.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>Table name → column names, parsed from every CREATE TABLE in the script.</summary>
    private static Dictionary<string, HashSet<string>> ParseCreateTables(string sql)
    {
        var tables = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach(Match m in Regex.Matches(sql, @"CREATE TABLE(?:\s+IF NOT EXISTS)?\s+(\w+)\s*\((.*?)\);",
                    RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach(var rawLine in m.Groups[2].Value.Split('\n'))
            {
                var line = rawLine.Trim();

                if(line.Length == 0 || line.StartsWith("--") ||
                   line.StartsWith("primary key", StringComparison.OrdinalIgnoreCase) ||
                   line.StartsWith("constraint", StringComparison.OrdinalIgnoreCase) ||
                   line.StartsWith("unique", StringComparison.OrdinalIgnoreCase))
                    continue;

                columns.Add(line.Split(new[] { ' ', '\t' }, 2)[0].TrimEnd(','));
            }

            tables[m.Groups[1].Value] = columns;
        }

        return tables;
    }

    private static IEnumerable<(string File, string Table, string[] Columns)> RepositoryWrites(string repoDir)
    {
        foreach(var file in Directory.GetFiles(repoDir, "*.cs"))
        {
            var source = File.ReadAllText(file);

            // INSERT INTO table(col, col, ...) and COPY table(col, col, ...)
            foreach(Match m in Regex.Matches(source, @"(?:INSERT INTO|COPY)\s+(\w+)\s*\(([^)]*)\)",
                        RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                var columns = m.Groups[2].Value
                    .Split(',')
                    .Select(c => c.Trim())
                    .Where(c => c.Length > 0 && !c.StartsWith("@"))
                    .ToArray();

                yield return (Path.GetFileName(file), m.Groups[1].Value, columns);
            }

            // UPDATE table SET col = ..., col = ... WHERE: the columns before each '='. Splitting on ',' also cuts
            // inside COALESCE(a, b); those fragments have no '=' at their start and are skipped.
            foreach(Match m in Regex.Matches(source, @"UPDATE\s+(\w+)\s+SET\s+([^""]*?)\s+WHERE",
                        RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                var columns = m.Groups[2].Value
                    .Split(',')
                    .Select(part => Regex.Match(part, @"^\s*(\w+)\s*="))
                    .Where(x => x.Success)
                    .Select(x => x.Groups[1].Value)
                    .ToArray();

                yield return (Path.GetFileName(file), m.Groups[1].Value, columns);
            }
        }
    }

    [Fact]
    public void Repository_writes_only_name_columns_that_createdb_creates()
    {
        var root = FindRepoRoot();
        var schema = ParseCreateTables(File.ReadAllText(Path.Combine(root, CreateDbScript)));
        var problems = new List<string>();

        Assert.Contains("blocks", schema.Keys);
        Assert.Contains("auxblocks", schema.Keys);

        foreach(var (file, table, columns) in RepositoryWrites(Path.Combine(root, RepositoryDir)))
        {
            if(!schema.TryGetValue(table, out var known))
            {
                problems.Add($"{file}: table '{table}' is not created by createdb.sql");
                continue;
            }

            foreach(var column in columns.Where(c => !known.Contains(c)))
                problems.Add($"{file}: {table}.{column} is written (INSERT, COPY or UPDATE) but not created by createdb.sql");
        }

        Assert.True(problems.Count == 0, "Schema drift between repositories and createdb.sql:" + Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Every_fork_added_column_has_a_migration_script()
    {
        // Columns that upstream's createdb.sql does not have. Each must be reachable on an
        // existing database through an ALTER TABLE ... ADD COLUMN IF NOT EXISTS script.
        var forkColumns = new[]
        {
            ("poolstats", "connectedworkers"),
            ("blocks", "worker"),
            ("blocks", "minereffort"),
            ("blocks", "difficulty"),
            ("auxblocks", "difficulty"),
            ("auxblocks", "networkdifficulty"),
        };

        var root = FindRepoRoot();
        var scriptsDir = Path.GetDirectoryName(Path.Combine(root, CreateDbScript))!;
        var migrations = Directory.GetFiles(scriptsDir, "add_*.sql").Select(File.ReadAllText).ToArray();

        foreach(var (table, column) in forkColumns)
        {
            var pattern = $@"ALTER TABLE\s+{table}\s+ADD COLUMN IF NOT EXISTS\s+{column}\b";
            Assert.True(migrations.Any(m => Regex.IsMatch(m, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline)),
                $"No add_*.sql migration adds {table}.{column}");
        }
    }
}
