using Microsoft.Data.Sqlite;
using YellowEditorX.Core;

// Usage: YellowEditorX.Tests <lists.json> <copy of wixoss.db>
int failures = 0;
void Check(bool ok, string what)
{
    if (!ok) { failures++; Console.WriteLine("FAIL " + what); }
}

var lists = CardLists.Load(args[0]);
string official = args[1];
Console.WriteLine($"lists: {lists.Kind.Count} kinds, {lists.Class.Count} classes, {lists.LrigType.Count} lrig types, {lists.LimitCond.Count} limit conditions, {lists.Team.Count} teams, {lists.Timing.Count} timings");
Check(lists.Kind.Contains("シグニ") && lists.Class.Count > 50 && lists.LrigType.Count > 50 && lists.Timing.Count == 3, "list counts");

string tmp = Path.Combine(Path.GetTempPath(), "wxe_test");
if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
Directory.CreateDirectory(tmp);

static List<(string, string)> Schema(string db)
{
    using var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
    con.Open();
    using var cmd = con.CreateCommand();
    cmd.CommandText = "SELECT name, sql FROM sqlite_master ORDER BY name";
    using var r = cmd.ExecuteReader();
    var list = new List<(string, string)>();
    while (r.Read()) list.Add((r.GetString(0), r.IsDBNull(1) ? "" : r.GetString(1)));
    return list;
}

// 1. a new pack has exactly the tables of wixoss.db (build_db.py)
string copy = Path.Combine(tmp, "copy", "Copy.db");
CardDb.Create(copy);
Check(Schema(copy).SequenceEqual(Schema(official)), "new pack schema = wixoss.db schema:\n" + string.Join("\n", Schema(copy).Except(Schema(official))));
Check(CardDb.IsPack(copy) && CardDb.IsPack(official), "both are packs");

// 2. every official card reads, writes and reads back the same
var all = CardDb.ReadAll(official);
Console.WriteLine($"official: {all.Count} cards, {all.Sum(c => c.Prints.Count)} prints, {all.Count(c => !c.Ko.IsEmpty)} with ko");
Check(all.Count > 7000 && all.All(c => c.IsOfficial && c.Prints.Count > 0 && c.Ja.Name.Length > 0), "official cards read");
CardDb.Write(copy, all);
var again = CardDb.ReadAll(copy);
var differ = all.Zip(again).Where(p => !p.First.SameAs(p.Second)).Select(p => p.First.Id).ToList();
Check(again.Count == all.Count && differ.Count == 0, $"round trip: {differ.Count} cards differ ({string.Join(",", differ.Take(10))})");
// The raw rows too: list columns and costs are written the way build_db.py writes them.
static List<string> Rows(string db, string sql)
{
    using var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
    con.Open();
    using var cmd = con.CreateCommand();
    cmd.CommandText = sql;
    using var r = cmd.ExecuteReader();
    var list = new List<string>();
    while (r.Read()) list.Add(string.Join("|", Enumerable.Range(0, r.FieldCount).Select(i => r.IsDBNull(i) ? "NULL" : r.GetValue(i).ToString())));
    return list;
}
const string ListCols = "SELECT cid,kind,class,limit_cond,team,timing,color,level,lim,power,cost_ena,cost_other,coin,guard,life_burst,source,alias FROM cards ORDER BY cid";
var rawDiff = Rows(official, ListCols).Except(Rows(copy, ListCols)).ToList();
Check(rawDiff.Count == 0, $"cards rows written like build_db.py: {rawDiff.Count} differ, first {rawDiff.FirstOrDefault()}");

// 3. the editor shows every official card and gives it back unchanged
var model = new EditorModel(lists);
int changed = 0;
foreach (var c in all)
{
    model.Load(c);
    if (!model.ToCard().SameAs(c) || model.IsDirty) { if (changed++ < 5) Console.WriteLine($"  editor changes {c.Id} {c.Name}"); }
}
Check(changed == 0, $"editor round trip: {changed} cards change");
Check(lists.AddFrom(all) >= 0 && lists.Kind.Count == 12, "lists cover the official kinds");

// 4. fields: colors, ∞, list fields, prints, dirty tracking
model.Load(new Card { Id = 1000001, Kind = { "シグニ" }, Color = 0, Prints = { new CardPrint { Idx = 1 } } });
Check(model.Colors.Single(c => c.Text == "無").IsChecked && model.ToCard().Color == 0, "無 = 0");
model.Colors.Single(c => c.Text == "赤").IsChecked = true;
Check(!model.Colors.Single(c => c.Text == "無").IsChecked && model.ToCard().Color == Card.Red && model.IsDirty, "a color turns 無 off");
model.Colors.Single(c => c.Text == "赤").IsChecked = false;
Check(model.ToCard().Color == null, "no color = null");
model.Power = "∞";
Check(model.ToCard().Power == -1 && model.Validate() == null, "∞ power");
model.Power = "-5";
Check(model.Validate() != null, "negative power refused");
model.Power = "";
model.Append(nameof(EditorModel.Classes), "精武：アーム");
model.Append(nameof(EditorModel.Classes), "精武：アーム");
model.Classes += "、 精生：水獣";
Check(model.ToCard().Class.SequenceEqual(new[] { "精武：アーム", "精生：水獣" }), "class list: " + model.Classes);
model.CostEna = "{R}{W/B}{C}";
Check(model.Validate() == null, "hybrid cost accepted");
model.CostOther = "{\"coin\": ";
Check(model.Validate() != null, "broken JSON refused");
model.CostOther = "";
var p2 = model.AddPrint();
Check(p2.Idx == 2 && model.SelectedPrint == p2 && model.PictureIdx == 2, "second print");
model.MovePrint(p2, -1);
Check(model.Prints[0] == p2 && p2.Idx == 1 && model.PictureIdx == 1, "moved up becomes idx 1");
model.RemovePrint(p2);
Check(model.Prints.Count == 1 && model.Prints[0].Idx == 1, "removed, renumbered");
Check(model.ShowPower && !model.ShowLimit && !model.ShowTiming, "シグニ fields");
model.Kinds.Single(k => k.Text == "シグニ").IsChecked = false;
model.Kinds.Single(k => k.Text == "アーツ").IsChecked = true;
Check(!model.ShowPower && model.ShowTiming && model.ShowCost, "アーツ fields");
model.Kinds.Single(k => k.Text == "アーツ").IsChecked = false;
Check(model.ShowPower && model.ShowLimit, "no kind: every field");

// 4b. formats: the card shows what any print has; changing it sets every print
var mixed = all.First(c => c.Prints.Select(p => p.Formats).Distinct().Count() > 1);
model.Load(mixed);
long anyFormat = mixed.Prints.Aggregate(0L, (f, p) => f | (p.Formats ?? 0));
Check(model.FormatKey == ((anyFormat & Card.FormatKey) != 0) && model.FormatDiva == ((anyFormat & Card.FormatDiva) != 0) && !model.IsDirty,
      $"formats of {mixed.Id}: any print counts, nothing changes on load");
model.FormatDiva = !model.FormatDiva;
Check(model.IsDirty && model.ToCard().Prints.Select(p => p.Formats).Distinct().Count() == 1, "a changed format goes to every print");
model.Load(new Card { Id = 1, Prints = { new CardPrint { Idx = 1, Formats = 3 } } });
Check(model.AddPrint().Formats == 3, "a new print gets the card's formats");

// 4c. the class column: signi classes or lrig types, by kind
Check(lists.Class.Contains("精武：アーム") && !lists.Class.Contains("タマ") && lists.LrigType.Contains("タマ"), "class and lrig type lists apart");
model.Load(new Card { Id = 1, Kind = { "シグニ" } });
Check(model.ClassLabel == "클래스" && model.ClassChoices == lists.Class, "signi: 클래스");
model.Kinds.Single(k => k.Text == "シグニ").IsChecked = false;
model.Kinds.Single(k => k.Text == "アシストルリグ").IsChecked = true;
Check(model.ClassLabel == "루리그 타입" && model.ClassChoices == lists.LrigType, "assist lrig: 루리그 타입");

// 4d. symbol buttons
var symbols = CardLists.LoadSymbols(Path.Combine(Path.GetDirectoryName(args[0])!, "symbols.json"));
Check(symbols.Select(s => s.Code).SequenceEqual(new[] { "《白》", "《赤》", "《青》", "《緑》", "《黒》", "《無》" }) && symbols.All(s => s.Label.Length > 0),
      "symbols.json: " + string.Join(" ", symbols.Select(s => s.Label)));
model.Ability = "【起】";
model.AppendSymbol("《白》");
Check(model.Ability == "【起】《白》", "symbol added at the end");

// 5. dates of an orica
model.Load(new Card { Id = 5, Source = Card.Orica, Created = "2026-01-01", Modified = "2026-01-02" });
model.Name = "변경";
var sv = model.ToSaveCard(new DateTime(2026, 11, 20));
Check(sv.Created == "2026-01-01" && sv.Modified == "2026-11-20", "modified date set on save");
model.Load(null, fromDb: false);
model.Code = "6";
sv = model.ToSaveCard(new DateTime(2026, 11, 20));
Check(sv.Created == "2026-11-20", "new card gets a created date");
model.Load(all[0]);
model.Name = "번역";
Check(!model.ToSaveCard(DateTime.Today).HasMeta, "official card gets no card_meta");

// 6. next free code
Check(Workspace.NextFreeCode(new HashSet<long> { 10, 11, 13 }, 10) == 12, "next free after 10");
Check(Workspace.NextFreeCode(new HashSet<long> { 10, 11, 13 }, 0) == 14, "next free after max");
Check(Workspace.NextFreeCode(new HashSet<long>(), 0) == 1000001, "next free in empty db");

// 7. updates: GitHub release answer, version compare, and the updater script on a temp folder
{
    string Release(string tag, string asset = "YellowEditorX-1.2.0.zip", bool pre = false) =>
        "{\"tag_name\":\"" + tag + "\",\"prerelease\":" + (pre ? "true" : "false") + ",\"draft\":false,\"body\":\"- 숨김 체크박스\\r\\n- 아이콘\","
        + "\"assets\":[{\"name\":\"notes.txt\",\"browser_download_url\":\"https://x/notes.txt\"},{\"name\":\"" + asset + "\",\"browser_download_url\":\"https://x/" + asset + "\"}]}";
    var cur = new Version(1, 1, 0);
    var u = Updates.Parse(Release("v1.2.0"), cur);
    Check(u != null && u.Version == new Version(1, 2, 0, 0) && u.ZipUrl == "https://x/YellowEditorX-1.2.0.zip" && u.Notes == "- 숨김 체크박스\n- 아이콘", "newer release found");
    Check(Updates.Parse(Release("v1.1"), cur) == null && Updates.Parse(Release("1.0.9"), cur) == null, "same or older release ignored");
    Check(Updates.Parse(Release("v1.2.0", pre: true), cur) == null && Updates.Parse(Release("v1.2.0", "x.7z"), cur) == null && Updates.Parse(Release("latest"), cur) == null,
          "prerelease, no zip, bad tag ignored");
    Check(Updates.Parse(Release("v1.1.0.1"), new Version(1, 1, 0, 0)) != null, "fourth number counts");

    string upd = Path.Combine(Path.GetTempPath(), "yex_update_test");
    if (Directory.Exists(upd)) Directory.Delete(upd, true);
    string app = Path.Combine(upd, "앱 폴더"), extracted = Path.Combine(upd, "new");
    string inner = Path.Combine(extracted, "YellowEditorX");
    Directory.CreateDirectory(Path.Combine(app, "data"));
    Directory.CreateDirectory(Path.Combine(inner, "data", "github"));
    File.WriteAllText(Path.Combine(app, "settings.json"), "mine");
    File.WriteAllText(Path.Combine(app, "old.dll"), "old");
    File.WriteAllText(Path.Combine(app, "data", "cardinfo_korean.txt"), "edited");
    File.WriteAllText(Path.Combine(inner, "settings.json"), "theirs");
    File.WriteAllText(Path.Combine(inner, "old.dll"), "new");
    File.WriteAllText(Path.Combine(inner, "data", "cardinfo_korean.txt"), "released");
    File.WriteAllText(Path.Combine(inner, "data", "github", "strings.conf"), "s");
    File.WriteAllText(Path.Combine(inner, "start.cmd"), "@echo started> \"%~dp0started.txt\"");
    string folder = Updates.ProgramFolder(extracted);
    Check(folder == inner, "zipped folder found inside the zip");
    string log = Path.Combine(upd, "update.log"), script = Path.Combine(upd, "update.ps1");
    File.WriteAllText(script, Updates.UpdaterScript(999999, folder, app, "start.cmd", log), new System.Text.UTF8Encoding(true));
    var ps = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell.exe",
        $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"") { UseShellExecute = false, CreateNoWindow = true })!;
    ps.WaitForExit(60000);
    for (int i = 0; i < 50 && !File.Exists(Path.Combine(app, "started.txt")); i++) Thread.Sleep(100);
    string R(params string[] p) => File.Exists(Path.Combine(p)) ? File.ReadAllText(Path.Combine(p)).Trim() : "(none)";
    Check(R(log) == "ok" && R(app, "old.dll") == "new" && R(app, "settings.json") == "mine" && R(app, "data", "github", "strings.conf") == "s",
          $"updater copied the files, kept settings: log={R(log)} dll={R(app, "old.dll")} settings={R(app, "settings.json")}");
    Check(R(app, "data", "cardinfo_korean.txt") == "released" && R(app, "data", "cardinfo_korean.txt.bak") == "edited", "changed cardinfo backed up");
    Check(R(app, "started.txt") == "started", "editor started again");
    Directory.Delete(upd, true);
}

// 8. card files: pics\<cid>.png, pics\<cid>_<idx>.png and the script move, copy, go and come back
string a = Path.Combine(tmp, "a", "A.db"), b = Path.Combine(tmp, "b", "B.db");
CardDb.Create(a); CardDb.Create(b);
var fa = new CardFiles(a); var fb = new CardFiles(b);
Check(fa.Picture(1) == Path.Combine(tmp, "a", "pics", "1.png") && fa.Picture(1, 3) == Path.Combine(tmp, "a", "pics", "1_3.png"), "picture names");
Directory.CreateDirectory(fa.PicsFolder);
File.WriteAllText(fa.Picture(1), "img");
File.WriteAllText(fa.Picture(1, 2), "img2");
File.WriteAllText(fa.Picture(10, 2), "other card");
var scr = fa.EnsureScript(new Card { Id = 1, Ko = { Name = "카드" } }, out bool made);
Check(made && File.ReadAllText(scr) == "--카드\r\nlocal s,id=GetID()\r\nfunction s.initial_effect(c)\r\n\t\r\nend\r\n", "script template");
Check(fa.All(1).Count == 3, "files of card 1 (not 10_2): " + string.Join(",", fa.All(1).Select(Path.GetFileName)));
CardFiles.Transfer(fa, 1, fa, 2, move: true, overwrite: false);
Check(!File.Exists(fa.Picture(1)) && File.ReadAllText(fa.Picture(2, 2)) == "img2" && File.Exists(fa.Script(2)), "move files to new code");
CardFiles.Transfer(fa, 2, fb, 2, move: false, overwrite: false);
Check(File.Exists(fa.Picture(2)) && File.Exists(fb.Picture(2)) && File.Exists(fb.Picture(2, 2)), "copy files to other db");
var removed = fb.Remove(2);
Check(removed.Count == 3 && !File.Exists(fb.Picture(2)), "remove files");
CardFiles.Restore(removed);
Check(File.ReadAllText(fb.Picture(2, 2)) == "img2", "restore files");

// 9. write / delete in one transaction; empty texts are not written
var sample = new Card { Id = 123, Kind = { "シグニ" }, Class = { "精武：アーム" }, Color = Card.Red, Level = 2, Power = 5000, Ko = { Name = "테스트", Ability = "1줄\r\n2줄" },
                        Prints = { new CardPrint { Idx = 1, CardNo = "T-001" } }, Maker = "시안" };
CardDb.Write(a, new[] { sample });
var read = CardDb.Read(a, 123)!;
Check(read.SameAs(sample), "write then read");
Check(Rows(a, "SELECT lang, ability FROM texts WHERE cid=123").SequenceEqual(new[] { "ko|1줄\n2줄" }), "only ko written, with \\n");
Check(Rows(a, "SELECT kind, class FROM cards WHERE cid=123").Single() == "[\"シグニ\"]|[\"精武：アーム\"]", "lists as JSON like build_db.py");
CardDb.Write(a, Array.Empty<Card>(), new long[] { 123 });
Check(CardDb.Read(a, 123) == null && Rows(a, "SELECT cid FROM texts UNION ALL SELECT cid FROM prints UNION ALL SELECT cid FROM card_meta").Count == 0, "delete from every table");

// 10. operations and undo
{
    string d1 = Path.Combine(tmp, "o1", "One.db"), d2 = Path.Combine(tmp, "o2", "Two.db");
    CardDb.Create(d1); CardDb.Create(d2);
    var f1 = new CardFiles(d1); var f2 = new CardFiles(d2);
    var hist = new History();
    var x = new Card { Id = 500, Ko = { Name = "원본" }, Kind = { "スペル" }, Prints = { new CardPrint { Idx = 1 }, new CardPrint { Idx = 2 } } };
    hist.Push(CardOps.Save(d1, x));
    Directory.CreateDirectory(f1.PicsFolder);
    File.WriteAllText(f1.Picture(500), "p500");
    File.WriteAllText(f1.Picture(500, 2), "p500_2");
    f1.EnsureScript(x, out _);
    var y = x.Clone(); y.Ko.Name = "수정";
    hist.Push(CardOps.Save(d1, y));
    Check(CardDb.Read(d1, 500)!.Name == "수정", "update");
    hist.Undo();
    Check(CardDb.Read(d1, 500)!.Name == "원본", "undo update");
    var moved = x.Clone(); moved.Id = 600;
    hist.Push(CardOps.ChangeCode(d1, 500, moved, move: true));
    Check(CardDb.Read(d1, 500) == null && CardDb.Read(d1, 600) != null && File.Exists(f1.Picture(600, 2)) && !File.Exists(f1.Picture(500)), "change code move");
    hist.Undo();
    Check(CardDb.Read(d1, 500) != null && CardDb.Read(d1, 600) == null && File.Exists(f1.Picture(500, 2)) && !File.Exists(f1.Picture(600)), "undo change code");
    hist.Push(CardOps.ChangeCode(d1, 500, moved, move: false));
    Check(CardDb.Read(d1, 500) != null && File.Exists(f1.Picture(600)) && File.Exists(f1.Picture(500)), "change code copy");
    hist.Undo();
    Check(CardDb.Read(d1, 600) == null && !File.Exists(f1.Picture(600)), "undo copy");
    // copy to another db, with an existing card that gets overwritten
    var old2 = new Card { Id = 500, Ko = { Name = "대상 원래" } };
    CardDb.Write(d2, new[] { old2 });
    Directory.CreateDirectory(f2.PicsFolder);
    File.WriteAllText(f2.Picture(500), "old2");
    hist.Push(CardOps.CopyTo(d1, new[] { x }, d2, overwrite: false, move: false, out int n0));
    Check(n0 == 0 && CardDb.Read(d2, 500)!.Name == "대상 원래", "copy skip existing");
    hist.Push(CardOps.CopyTo(d1, new[] { x }, d2, overwrite: true, move: false, out int n1));
    Check(n1 == 1 && CardDb.Read(d2, 500)!.Name == "원본" && File.ReadAllText(f2.Picture(500)) == "p500" && File.Exists(f2.Picture(500, 2)) && File.Exists(f2.Script(500)),
          "copy overwrite with files");
    hist.Undo();
    Check(CardDb.Read(d2, 500)!.Name == "대상 원래" && File.ReadAllText(f2.Picture(500)) == "old2" && !File.Exists(f2.Script(500)) && !File.Exists(f2.Picture(500, 2)),
          "undo copy overwrite");
    hist.Undo(); // the skipped copy (no change)
    var z = new Card { Id = 700, Ko = { Name = "이동" } };
    CardDb.Write(d1, new[] { z });
    File.WriteAllText(f1.Picture(700), "p700");
    hist.Push(CardOps.CopyTo(d1, new[] { z }, d2, overwrite: false, move: true, out _));
    Check(CardDb.Read(d1, 700) == null && CardDb.Read(d2, 700) != null && File.Exists(f2.Picture(700)) && !File.Exists(f1.Picture(700)), "move to other db");
    hist.Undo();
    Check(CardDb.Read(d1, 700) != null && CardDb.Read(d2, 700) == null && File.Exists(f1.Picture(700)) && !File.Exists(f2.Picture(700)), "undo move");
    hist.Push(CardOps.Delete(d1, new[] { x }, withFiles: true));
    Check(CardDb.Read(d1, 500) == null && !File.Exists(f1.Picture(500)) && !File.Exists(f1.Picture(500, 2)) && !File.Exists(f1.Script(500)), "delete with files");
    hist.Undo();
    Check(CardDb.Read(d1, 500)!.Name == "원본" && File.ReadAllText(f1.Picture(500, 2)) == "p500_2" && File.Exists(f1.Script(500)), "undo delete");
    hist.Push(CardOps.ReplaceImage(d1, 500, 2, p => File.WriteAllText(p, "new")));
    Check(File.ReadAllText(f1.Picture(500, 2)) == "new", "replace image of idx 2");
    hist.Undo();
    Check(File.ReadAllText(f1.Picture(500, 2)) == "p500_2", "undo replace image");
    hist.Push(CardOps.ReplaceImage(d1, 500, 3, p => File.WriteAllText(p, "new3")));
    hist.Undo();
    Check(!File.Exists(f1.Picture(500, 3)), "undo a new image removes it");
}

Directory.Delete(tmp, true);
Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURES");
return failures == 0 ? 0 : 1;
