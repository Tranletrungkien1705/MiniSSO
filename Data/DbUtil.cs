namespace MiniSSO.Data;

public static class DbUtil
{
    public static bool IsPostgres(string conn) => conn.StartsWith("postgres", System.StringComparison.OrdinalIgnoreCase);

    public static string ToNpgsql(string uri)
    {
        var u = new System.Uri(uri);
        var ui = u.UserInfo.Split(':', 2);
        var db = System.Uri.UnescapeDataString(u.AbsolutePath.TrimStart('/'));
        var sb = new System.Text.StringBuilder();
        sb.Append($"Host={u.Host};Port={(u.Port > 0 ? u.Port : 5432)};Database={db};");
        sb.Append($"Username={System.Uri.UnescapeDataString(ui[0])};Password={System.Uri.UnescapeDataString(ui.Length > 1 ? ui[1] : "")};");
        sb.Append("SSL Mode=Require;Pooling=true;");
        foreach (var kv in u.Query.TrimStart('?').Split('&', System.StringSplitOptions.RemoveEmptyEntries))
        {
            var p = kv.Split('=', 2);
            if (p.Length == 2 && p[0].Equals("channel_binding", System.StringComparison.OrdinalIgnoreCase))
                sb.Append($"Channel Binding={p[1]};");
        }
        return sb.ToString();
    }
}
