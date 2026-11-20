namespace YellowEditorX.Core;

/// <summary>Image and script files that belong to a card, next to its pack .db: pics/&lt;cid&gt;.png, pics/&lt;cid&gt;_&lt;idx&gt;.png, script/c&lt;cid&gt;.lua.</summary>
public sealed class CardFiles
{
    public readonly string Root;
    public CardFiles(string dbPath) => Root = Path.GetDirectoryName(Path.GetFullPath(dbPath))!;

    public string PicsFolder => Path.Combine(Root, "pics");
    /// <summary>idx 1 is pics/&lt;cid&gt;.png, the others pics/&lt;cid&gt;_&lt;idx&gt;.png.</summary>
    public string Picture(long id, int idx = 1) => Path.Combine(PicsFolder, idx <= 1 ? id + ".png" : $"{id}_{idx}.png");
    public string Script(long id) => Path.Combine(Root, "script", "c" + id + ".lua");
    public string ScriptFolder => Path.Combine(Root, "script");

    /// <summary>The files of the card that exist: every picture and the script.</summary>
    public List<string> All(long id)
    {
        var list = new List<string>();
        if (File.Exists(Picture(id))) list.Add(Picture(id));
        if (Directory.Exists(PicsFolder))
            list.AddRange(Directory.GetFiles(PicsFolder, id + "_*.png").Where(f => IdxOf(id, f) > 1).OrderBy(f => IdxOf(id, f)));
        if (File.Exists(Script(id))) list.Add(Script(id));
        return list;
    }

    /// <summary>The idx of a picture file of the card, or 0 when the file isn't one.</summary>
    static int IdxOf(long id, string file)
    {
        string name = Path.GetFileNameWithoutExtension(file);
        if (name == id.ToString()) return 1;
        return name.StartsWith(id + "_") && int.TryParse(name[(id.ToString().Length + 1)..], out int i) ? i : 0;
    }

    /// <summary>Where a file of oldId goes for newId in the folder of <paramref name="to"/>.</summary>
    public string Counterpart(string file, long oldId, CardFiles to, long newId)
    {
        if (string.Equals(file, Script(oldId), StringComparison.OrdinalIgnoreCase)) return to.Script(newId);
        return to.Picture(newId, IdxOf(oldId, file));
    }

    /// <summary>Content of a file that was removed, so it can be put back.</summary>
    public sealed record Saved(string Path, byte[] Data);

    /// <summary>Moves (or copies) every existing file of oldId to newId. Returns (from, to) of the files written.</summary>
    public static List<(string From, string To)> Transfer(CardFiles from, long oldId, CardFiles to, long newId, bool move, bool overwrite)
    {
        var done = new List<(string, string)>();
        foreach (var src in from.All(oldId))
        {
            string dst = from.Counterpart(src, oldId, to, newId);
            if (string.Equals(Path.GetFullPath(src), Path.GetFullPath(dst), StringComparison.OrdinalIgnoreCase)) continue;
            if (File.Exists(dst) && !overwrite) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            if (move) File.Move(src, dst, overwrite);
            else File.Copy(src, dst, overwrite);
            done.Add((src, dst));
        }
        return done;
    }

    /// <summary>Reads then deletes every file of the card; the returned content allows undo.</summary>
    public List<Saved> Remove(long id)
    {
        var saved = new List<Saved>();
        foreach (var f in All(id))
        {
            saved.Add(new Saved(f, File.ReadAllBytes(f)));
            File.Delete(f);
        }
        return saved;
    }

    public static void Restore(IEnumerable<Saved> files)
    {
        foreach (var s in files)
        {
            if (File.Exists(s.Path)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(s.Path)!);
            File.WriteAllBytes(s.Path, s.Data);
        }
    }

    /// <summary>Creates script/c&lt;cid&gt;.lua from the YGOPro-style template, if it doesn't exist.</summary>
    public string EnsureScript(Card c, out bool created)
    {
        string lua = Script(c.Id);
        created = false;
        if (File.Exists(lua)) return lua;
        Directory.CreateDirectory(Path.GetDirectoryName(lua)!);
        File.WriteAllText(lua,
            "--" + c.Name + "\r\nlocal s,id=GetID()\r\nfunction s.initial_effect(c)\r\n\t\r\nend\r\n",
            new System.Text.UTF8Encoding(false));
        created = true;
        return lua;
    }
}
