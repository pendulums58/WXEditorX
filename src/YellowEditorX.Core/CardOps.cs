namespace YellowEditorX.Core;

/// <summary>Database changes the editor makes. Each returns a history entry that undoes it.</summary>
public static class CardOps
{
    /// <summary>Inserts or overwrites the card under its own code.</summary>
    public static HistoryEntry Save(string db, Card c)
    {
        var h = new HistoryEntry { Description = $"저장: {c.Name}" };
        h.Capture(db, new[] { c.Id });
        CardDb.Write(db, new[] { c });
        return h;
    }

    /// <summary>
    /// Saves an edited card under a new code. move: the old code is deleted and its images/script are renamed;
    /// otherwise the old card stays and its files are copied.
    /// </summary>
    public static HistoryEntry ChangeCode(string db, long oldId, Card c, bool move)
    {
        var h = new HistoryEntry { Description = move ? $"코드 변경: {oldId} → {c.Id}" : $"복사본 저장: {c.Id}" };
        h.Capture(db, move ? new[] { oldId, c.Id } : new[] { c.Id });
        var files = new CardFiles(db);
        var done = CardFiles.Transfer(files, oldId, files, c.Id, move, overwrite: false);
        if (move) h.MovedFiles.AddRange(done);
        else h.CreatedFiles.AddRange(done.Select(d => d.To));
        CardDb.Write(db, new[] { c }, move ? new[] { oldId } : null);
        return h;
    }

    public static HistoryEntry Delete(string db, IReadOnlyList<Card> cards, bool withFiles)
    {
        var h = new HistoryEntry { Description = cards.Count == 1 ? $"삭제: {cards[0].Name}" : $"{cards.Count}장 삭제" };
        h.Capture(db, cards.Select(c => c.Id));
        if (withFiles)
        {
            var files = new CardFiles(db);
            foreach (var c in cards) h.RemovedFiles.AddRange(files.Remove(c.Id));
        }
        CardDb.Write(db, Array.Empty<Card>(), cards.Select(c => c.Id));
        return h;
    }

    /// <summary>
    /// Copies (or moves) cards with their images and script to another database.
    /// Cards whose code already exists there are overwritten only when overwrite is true.
    /// </summary>
    public static HistoryEntry CopyTo(string fromDb, IReadOnlyList<Card> cards, string toDb, bool overwrite, bool move, out int copied)
    {
        var h = new HistoryEntry { Description = (move ? "이동: " : "복사: ") + $"{cards.Count}장 → {Path.GetFileName(toDb)}" };
        var existing = CardDb.ReadIds(toDb);
        var todo = cards.Where(c => overwrite || !existing.Contains(c.Id)).ToList();
        copied = todo.Count;
        if (todo.Count == 0) return h;
        h.Capture(toDb, todo.Select(c => c.Id));
        if (move) h.Capture(fromDb, todo.Select(c => c.Id));
        var src = new CardFiles(fromDb);
        var dst = new CardFiles(toDb);
        foreach (var c in todo)
        {
            // Files that get overwritten are kept so undo can put them back.
            if (overwrite)
                foreach (var f in src.All(c.Id).Select(s => src.Counterpart(s, c.Id, dst, c.Id)).Where(File.Exists))
                    h.RemovedFiles.Add(new CardFiles.Saved(f, File.ReadAllBytes(f)));
            foreach (var (from, to) in CardFiles.Transfer(src, c.Id, dst, c.Id, move, overwrite))
            {
                if (move) h.MovedFiles.Add((from, to));
                else h.CreatedFiles.Add(to);
            }
        }
        CardDb.Write(toDb, todo);
        if (move) CardDb.Write(fromDb, Array.Empty<Card>(), todo.Select(c => c.Id));
        return h;
    }

    /// <summary>Replaces one picture of the card; write() is given the path to write.</summary>
    public static HistoryEntry ReplaceImage(string db, long id, int idx, Action<string> write)
    {
        var h = new HistoryEntry { Description = $"이미지 변경: {id}" + (idx > 1 ? $" ({idx})" : "") };
        string pic = new CardFiles(db).Picture(id, idx);
        if (File.Exists(pic)) h.RemovedFiles.Add(new CardFiles.Saved(pic, File.ReadAllBytes(pic)));
        // Undo first deletes created files, then restores removed ones: for an overwritten file that means
        // deleting the new image and writing the old bytes back.
        h.CreatedFiles.Add(pic);
        Directory.CreateDirectory(Path.GetDirectoryName(pic)!);
        write(pic);
        return h;
    }
}
