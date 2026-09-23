using Microsoft.Data.Sqlite;

// Usage:
//   _svndump <wc.db> list <filter>            -> list "hash<TAB>path"
//   _svndump <wc.db> cat <path-substring>     -> print content of first matching file
var db = args[0];
var mode = args.Length > 1 ? args[1] : "list";
var filter = args.Length > 2 ? args[2] : "";
var pristineRoot = Path.Combine(Path.GetDirectoryName(db)!, "pristine");

using var conn = new SqliteConnection($"Data Source={db};Mode=ReadOnly");
conn.Open();
using var cmd = conn.CreateCommand();
cmd.CommandText = "SELECT local_relpath, checksum FROM NODES WHERE checksum IS NOT NULL";
using var r = cmd.ExecuteReader();
var rows = new List<(string path, string sum)>();
while (r.Read())
{
    var path = r.GetString(0);
    var sum = r.GetString(1);
    if (filter.Length > 0 && !path.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
    rows.Add((path, sum));
}

if (mode == "list")
{
    foreach (var (path, sum) in rows) Console.WriteLine($"{sum}\t{path}");
    return;
}

// cat
foreach (var (path, sum) in rows)
{
    var hex = sum.Replace("$sha1$", "");
    var file = Path.Combine(pristineRoot, hex[..2], hex + ".svn-base");
    Console.Error.WriteLine($"TRY: [{file}] exists={File.Exists(file)}");
    if (!File.Exists(file)) { Console.Error.WriteLine($"MISSING pristine: {file}"); continue; }
    Console.WriteLine($"===== {path} =====");
    Console.WriteLine(File.ReadAllText(file));
    break;
}
