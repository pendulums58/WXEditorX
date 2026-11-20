using System.Text.Json;

namespace YellowEditorX.Core;

/// <summary>
/// Choices for the list fields, from data\lists.json (values of the official wixoss.db, most used first).
/// Values a pack uses that aren't there are added when the pack is opened.
/// </summary>
public sealed class CardLists
{
    public List<string> Kind = new(), Class = new(), LimitCond = new(), Team = new(), Timing = new();

    public static CardLists Load(string path) => Parse(File.ReadAllText(path));

    public static CardLists Parse(string json)
    {
        var map = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json) ?? new();
        List<string> Get(string key) => map.TryGetValue(key, out var v) ? v : new();
        return new CardLists
        {
            Kind = Get("kind"), Class = Get("class"), LimitCond = Get("limit_cond"), Team = Get("team"), Timing = Get("timing"),
        };
    }

    /// <summary>Adds what the cards use and the lists don't have yet. Returns how many were added.</summary>
    public int AddFrom(IEnumerable<Card> cards)
    {
        int n = 0;
        void Add(List<string> list, IEnumerable<string> values)
        {
            foreach (var v in values)
                if (v.Length > 0 && !list.Contains(v)) { list.Add(v); n++; }
        }
        foreach (var c in cards)
        {
            Add(Kind, c.Kind); Add(Class, c.Class); Add(LimitCond, c.LimitCond); Add(Team, c.Team); Add(Timing, c.Timing);
        }
        return n;
    }
}
