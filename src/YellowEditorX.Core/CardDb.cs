using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace YellowEditorX.Core;

/// <summary>Reads and writes WXCore pack .db files (SQLite with cards, prints, card_meta and texts tables).</summary>
public static class CardDb
{
    /// <summary>The same CREATE statements as WixossArchive\build_db.py; a new pack must match it exactly.</summary>
    public const string CreateSql = """
    CREATE TABLE cards(
      cid INTEGER PRIMARY KEY,
      kind TEXT,          -- JSON 목록: ["シグニ","クラフト"]
      class TEXT,         -- JSON 목록: 시그니 클래스 / 루리그 타입
      color INTEGER,      -- 비트: 白1 赤2 青4 緑8 黒16, 0=무색, NULL=없음
      level INTEGER, lim INTEGER, power INTEGER,   -- -1 = ∞
      cost_ena TEXT,      -- '{R}{C}{C}'
      cost_other TEXT,    -- JSON: {"coin":2}
      grow_ena TEXT, grow_other TEXT,
      limit_cond TEXT,    -- JSON 목록: ["タマ","イオナ"]
      team TEXT,          -- JSON 목록
      coin INTEGER,       -- 오픈 시 얻는 코인
      guard INTEGER,      -- 가드 아이콘
      life_burst INTEGER,
      timing TEXT,        -- JSON 목록: ["メインフェイズ","アタックフェイズ"]
      source TEXT,        -- 'official' | 'orica'
      alias INTEGER       -- 동명 취급: 이름이 같은 다른 카드의 대표 cid. 0 = 없음(자기가 대표)
    );
    CREATE TABLE prints(  -- 인쇄본(일러스트) 목록. 카드 하나에 여러 장, 같은 카드 번호에 여러 장도 가능
      cid INTEGER, idx INTEGER,   -- idx: 1부터. 1 = 기본 일러스트
      card_no TEXT, set_code TEXT, rarity TEXT,
      illustrator TEXT, image TEXT, formats INTEGER, raw_name TEXT,
      PRIMARY KEY(cid, idx)
    );
    CREATE TABLE card_meta( -- 오리카 제작 정보. 공식 카드는 비어 있음
      cid INTEGER PRIMARY KEY, maker TEXT, tags TEXT, pack TEXT,
      created TEXT, modified TEXT
    );
    CREATE TABLE texts(
      cid INTEGER, lang TEXT, name TEXT, reading TEXT,
      ability TEXT, life_burst_text TEXT, flavor TEXT,
      PRIMARY KEY(cid, lang)
    );
    CREATE INDEX prints_no ON prints(card_no);
""";

    static readonly string[] Tables = { "cards", "prints", "card_meta", "texts" };

    static SqliteConnection Open(string path, bool write)
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = write ? SqliteOpenMode.ReadWrite : SqliteOpenMode.ReadOnly,
            // Without pooling the file is released as soon as we are done, so other
            // programs (the simulator, Explorer) never see it locked.
            Pooling = false,
        };
        var con = new SqliteConnection(cs.ToString());
        con.Open();
        return con;
    }

    public static void Create(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var cs = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false };
        using var con = new SqliteConnection(cs.ToString());
        con.Open();
        using var cmd = con.CreateCommand();
        // \n like build_db.py, whatever line endings this file was checked out with
        cmd.CommandText = CreateSql.Replace("\r\n", "\n");
        cmd.ExecuteNonQuery();
    }

    /// <summary>True when the file has the four pack tables.</summary>
    public static bool IsPack(string path)
    {
        try
        {
            using var con = Open(path, false);
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name IN ('cards','prints','card_meta','texts')";
            return Convert.ToInt64(cmd.ExecuteScalar()) == Tables.Length;
        }
        catch { return false; }
    }

    public static List<Card> ReadAll(string path) => Query(path, null);

    public static Card? Read(string path, long id)
    {
        var list = Query(path, id);
        return list.Count > 0 ? list[0] : null;
    }

    public static HashSet<long> ReadIds(string path)
    {
        var ids = new HashSet<long>();
        using var con = Open(path, false);
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT cid FROM cards";
        using var r = cmd.ExecuteReader();
        while (r.Read()) ids.Add(r.GetInt64(0));
        return ids;
    }

    static List<Card> Query(string path, long? id)
    {
        using var con = Open(path, false);
        string where = id != null ? " WHERE cid=$id" : "";
        SqliteDataReader Run(string sql)
        {
            var cmd = con.CreateCommand();
            cmd.CommandText = sql + where;
            if (id != null) cmd.Parameters.AddWithValue("$id", id.Value);
            return cmd.ExecuteReader();
        }

        var cards = new Dictionary<long, Card>();
        var order = new List<Card>();
        using (var r = Run("SELECT cid,kind,class,color,level,lim,power,cost_ena,cost_other,grow_ena,grow_other," +
                           "limit_cond,team,coin,guard,life_burst,timing,source,alias FROM cards"))
            while (r.Read())
            {
                var c = new Card
                {
                    Id = r.GetInt64(0),
                    Kind = List(S(r, 1)), Class = List(S(r, 2)),
                    Color = N(r, 3), Level = N(r, 4), Limit = N(r, 5), Power = N(r, 6),
                    CostEna = r.IsDBNull(7) ? null : r.GetString(7), CostOther = S(r, 8),
                    GrowEna = r.IsDBNull(9) ? null : r.GetString(9), GrowOther = S(r, 10),
                    LimitCond = List(S(r, 11)), Team = List(S(r, 12)),
                    Coin = N(r, 13), Guard = (N(r, 14) ?? 0) != 0, LifeBurst = (N(r, 15) ?? 0) != 0,
                    Timing = List(S(r, 16)), Source = S(r, 17), Alias = N(r, 18) ?? 0,
                };
                cards[c.Id] = c;
                order.Add(c);
            }
        using (var r = Run("SELECT cid,lang,name,reading,ability,life_burst_text,flavor FROM texts"))
            while (r.Read())
            {
                if (!cards.TryGetValue(r.GetInt64(0), out var c)) continue;
                var t = new CardText
                {
                    Name = S(r, 2), Reading = S(r, 3),
                    Ability = NormalizeNewlines(S(r, 4)), LifeBurst = NormalizeNewlines(S(r, 5)), Flavor = NormalizeNewlines(S(r, 6)),
                };
                if (S(r, 1) == "ko") c.Ko = t;
                else if (S(r, 1) == "ja") c.Ja = t;
            }
        using (var r = Run("SELECT cid,idx,card_no,set_code,rarity,illustrator,image,formats,raw_name FROM prints"))
            while (r.Read())
                if (cards.TryGetValue(r.GetInt64(0), out var c))
                    c.Prints.Add(new CardPrint
                    {
                        Idx = (int)r.GetInt64(1), CardNo = S(r, 2), SetCode = S(r, 3), Rarity = S(r, 4),
                        Illustrator = S(r, 5), Image = S(r, 6), Formats = N(r, 7), RawName = S(r, 8),
                    });
        using (var r = Run("SELECT cid,maker,tags,pack,created,modified FROM card_meta"))
            while (r.Read())
                if (cards.TryGetValue(r.GetInt64(0), out var c))
                {
                    c.Maker = S(r, 1); c.Tags = S(r, 2); c.Pack = S(r, 3); c.Created = S(r, 4); c.Modified = S(r, 5);
                }
        foreach (var c in order) c.Prints.Sort((a, b) => a.Idx.CompareTo(b.Idx));
        return order;
    }

    static long? N(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt64(i);
    static string S(SqliteDataReader r, int i) => r.IsDBNull(i) ? "" : r.GetString(i);

    static List<string> List(string json)
    {
        if (json.Length == 0) return new();
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new(); }
        catch (JsonException) { return new() { json }; }
    }

    static readonly JsonSerializerOptions JsonOut = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    /// <summary>Same as Python json.dumps(ensure_ascii=False): ["a", "b"].</summary>
    static string Json(List<string> list) =>
        "[" + string.Join(", ", list.Select(s => JsonSerializer.Serialize(s, JsonOut))) + "]";

    static object Db(long? v) => v == null ? DBNull.Value : v.Value;
    static object Db(string s) => s.Length == 0 ? DBNull.Value : s;

    /// <summary>Card text is edited with Windows line breaks; the database keeps \n.</summary>
    public static string NormalizeNewlines(string s) =>
        s.IndexOf('\n') < 0 ? s : s.Replace("\r\n", "\n").Replace("\n", "\r\n");
    static string Stored(string s) => s.Replace("\r\n", "\n");

    /// <summary>Inserts or replaces the cards and deletes the given ids, all in one transaction.</summary>
    public static void Write(string path, IEnumerable<Card> upserts, IEnumerable<long>? deletes = null)
    {
        var cards = upserts.ToList();
        using var con = Open(path, true);
        using var tr = con.BeginTransaction();
        void Exec(string sql, params (string, object)[] args)
        {
            using var cmd = con.CreateCommand();
            cmd.Transaction = tr;
            cmd.CommandText = sql;
            foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
            cmd.ExecuteNonQuery();
        }
        void Remove(long id)
        {
            foreach (var t in Tables) Exec($"DELETE FROM {t} WHERE cid=$id", ("$id", id));
        }
        foreach (long id in deletes ?? Array.Empty<long>()) Remove(id);
        foreach (var c in cards)
        {
            Remove(c.Id);
            Exec("INSERT INTO cards VALUES($cid,$kind,$class,$color,$level,$lim,$power,$cost_ena,$cost_other,$grow_ena,$grow_other," +
                 "$limit_cond,$team,$coin,$guard,$life_burst,$timing,$source,$alias)",
                ("$cid", c.Id), ("$kind", Json(c.Kind)), ("$class", Json(c.Class)), ("$color", Db(c.Color)),
                ("$level", Db(c.Level)), ("$lim", Db(c.Limit)), ("$power", Db(c.Power)),
                ("$cost_ena", (object?)c.CostEna ?? DBNull.Value), ("$cost_other", Db(c.CostOther)),
                ("$grow_ena", (object?)c.GrowEna ?? DBNull.Value), ("$grow_other", Db(c.GrowOther)),
                ("$limit_cond", Json(c.LimitCond)), ("$team", Json(c.Team)), ("$coin", Db(c.Coin)),
                ("$guard", c.Guard ? 1 : 0), ("$life_burst", c.LifeBurst ? 1 : 0), ("$timing", Json(c.Timing)),
                ("$source", c.Source), ("$alias", c.Alias));
            foreach (var (lang, t) in new[] { ("ja", c.Ja), ("ko", c.Ko) })
            {
                if (t.IsEmpty) continue;
                Exec("INSERT INTO texts VALUES($cid,$lang,$name,$reading,$ability,$lb,$flavor)",
                    ("$cid", c.Id), ("$lang", lang), ("$name", t.Name), ("$reading", Db(t.Reading)),
                    ("$ability", Stored(t.Ability)), ("$lb", Db(Stored(t.LifeBurst))), ("$flavor", Db(Stored(t.Flavor))));
            }
            foreach (var p in c.Prints)
                Exec("INSERT INTO prints VALUES($cid,$idx,$no,$set,$rarity,$ill,$image,$formats,$raw)",
                    ("$cid", c.Id), ("$idx", p.Idx), ("$no", Db(p.CardNo)), ("$set", Db(p.SetCode)), ("$rarity", Db(p.Rarity)),
                    ("$ill", Db(p.Illustrator)), ("$image", Db(p.Image)), ("$formats", Db(p.Formats)), ("$raw", Db(p.RawName)));
            if (c.HasMeta)
                Exec("INSERT INTO card_meta VALUES($cid,$maker,$tags,$pack,$created,$modified)",
                    ("$cid", c.Id), ("$maker", c.Maker), ("$tags", c.Tags), ("$pack", c.Pack), ("$created", c.Created), ("$modified", c.Modified));
        }
        tr.Commit();
    }
}
