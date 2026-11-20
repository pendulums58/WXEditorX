using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace YellowEditorX.Core;

public abstract class Notifier : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}

/// <summary>One toggle chip: a kind, a color or a timing.</summary>
public sealed class FlagOption : Notifier
{
    readonly Action<FlagOption> changed;
    bool isChecked;
    public long Value { get; }
    public string Text { get; }
    public FlagOption(long value, string text, Action<FlagOption> changed)
    {
        Value = value;
        Text = text;
        this.changed = changed;
    }
    public bool IsChecked
    {
        get => isChecked;
        set { if (Set(ref isChecked, value)) changed(this); }
    }
    internal void SetSilently(bool v) => Set(ref isChecked, v, nameof(IsChecked));
}

/// <summary>One row of the print list, as typed.</summary>
public sealed class PrintRow : Notifier
{
    readonly Action changed;
    int idx;
    string cardNo = "", setCode = "", rarity = "", illustrator = "", image = "";
    /// <summary>The name as printed on the official card; not edited, kept as it was.</summary>
    public string RawName = "";
    /// <summary>Format bits of this print. Edited for the whole card (see <see cref="EditorModel.FormatKey"/>).</summary>
    public long? Formats;

    public PrintRow(CardPrint p, Action changed)
    {
        this.changed = changed;
        idx = p.Idx; cardNo = p.CardNo; setCode = p.SetCode; rarity = p.Rarity; illustrator = p.Illustrator; image = p.Image;
        Formats = p.Formats;
        RawName = p.RawName;
    }

    void S(ref string f, string? v, [CallerMemberName] string? name = null) { if (Set(ref f, v ?? "", name)) changed(); }
    public int Idx { get => idx; internal set { if (Set(ref idx, value)) changed(); } }
    public string CardNo { get => cardNo; set => S(ref cardNo, value); }
    public string SetCode { get => setCode; set => S(ref setCode, value); }
    public string Rarity { get => rarity; set => S(ref rarity, value); }
    public string Illustrator { get => illustrator; set => S(ref illustrator, value); }
    /// <summary>The official image URL; empty for an orica (its picture is pics\&lt;cid&gt;.png).</summary>
    public string Image { get => image; set => S(ref image, value); }

    public CardPrint ToPrint() => new()
    {
        Idx = idx, CardNo = cardNo.Trim(), SetCode = setCode.Trim(), Rarity = rarity.Trim(), Illustrator = illustrator.Trim(),
        Image = image.Trim(), RawName = RawName, Formats = Formats,
    };
}

/// <summary>
/// Everything the editor shows for one card. Fields are kept as the user typed them;
/// ToCard() turns them back into database rows.
/// </summary>
public sealed class EditorModel : Notifier
{
    public CardLists Lists { get; }
    public ObservableCollection<FlagOption> Kinds { get; } = new();
    public List<FlagOption> Colors { get; } = new();
    public ObservableCollection<FlagOption> Timings { get; } = new();
    public ObservableCollection<PrintRow> Prints { get; } = new();

    const long Colorless = 0x100;

    string code = "", alias = "", name = "", reading = "", ability = "", lbText = "", flavor = "";
    string level = "", limit = "", power = "", coin = "";
    string costEna = "", costOther = "", growEna = "", growOther = "";
    string classes = "", limitCond = "", team = "";
    string maker = "", tags = "", pack = "", created = "", modified = "";
    bool guard, lifeBurst;
    string source = Card.Orica;
    CardText ja = new();
    /// <summary>The card as loaded: an emptied cost goes back to its null or "", and list order is kept.</summary>
    Card loaded = new();
    PrintRow? selectedPrint;
    bool loading, isDirty;
    Card baseline = new();

    /// <summary>The card as it is in the database, or null for a card that hasn't been saved yet.</summary>
    public Card? Original { get; private set; }

    public event Action? Changed;

    public EditorModel(CardLists lists)
    {
        Lists = lists;
        foreach (var k in lists.Kind) AddChip(Kinds, k, OnKindChanged);
        foreach (var (bit, text) in Card.Colors) Colors.Add(new FlagOption(bit, text, OnColorChanged));
        Colors.Add(new FlagOption(Colorless, "無", OnColorChanged));
        foreach (var t in lists.Timing) AddChip(Timings, t, _ => OnChanged());
        Prints.CollectionChanged += (_, _) => { Renumber(); RaiseFormats(); OnChanged(); };
        Load(null);
    }

    static void AddChip(ObservableCollection<FlagOption> chips, string text, Action<FlagOption> changed)
    {
        if (chips.All(c => c.Text != text)) chips.Add(new FlagOption(chips.Count, text, changed));
    }

    #region fields
    void S(ref string f, string? v, [CallerMemberName] string? name = null) { if (Set(ref f, v ?? "", name)) OnChanged(); }
    public string Code { get => code; set => S(ref code, value); }
    public string Alias { get => alias; set => S(ref alias, value); }
    public string Name { get => name; set => S(ref name, value); }
    public string Reading { get => reading; set => S(ref reading, value); }
    public string Ability { get => ability; set => S(ref ability, value); }
    public string LbText { get => lbText; set => S(ref lbText, value); }
    public string Flavor { get => flavor; set => S(ref flavor, value); }
    public string Level { get => level; set => S(ref level, value); }
    public string Limit { get => limit; set => S(ref limit, value); }
    public string Power { get => power; set => S(ref power, value); }
    public string Coin { get => coin; set => S(ref coin, value); }
    public string CostEna { get => costEna; set => S(ref costEna, value); }
    public string CostOther { get => costOther; set => S(ref costOther, value); }
    public string GrowEna { get => growEna; set => S(ref growEna, value); }
    public string GrowOther { get => growOther; set => S(ref growOther, value); }
    /// <summary>List fields are typed as "a, b".</summary>
    public string Classes { get => classes; set => S(ref classes, value); }
    public string LimitCond { get => limitCond; set => S(ref limitCond, value); }
    public string Team { get => team; set => S(ref team, value); }
    public string Maker { get => maker; set => S(ref maker, value); }
    public string Tags { get => tags; set => S(ref tags, value); }
    public string Pack { get => pack; set => S(ref pack, value); }
    public string Created { get => created; set => S(ref created, value); }
    public string Modified { get => modified; set => S(ref modified, value); }
    public bool Guard { get => guard; set { if (Set(ref guard, value)) OnChanged(); } }
    public bool LifeBurst { get => lifeBurst; set { if (Set(ref lifeBurst, value)) OnChanged(); } }

    // A card can be played in a format when any of its prints is marked for it, so the formats are shown
    // for the whole card; changing one sets it on every print.
    long CardFormats => Prints.Aggregate(0L, (f, p) => f | (p.Formats ?? 0));
    public bool FormatKey { get => (CardFormats & Card.FormatKey) != 0; set => SetFormat(Card.FormatKey, value); }
    public bool FormatDiva { get => (CardFormats & Card.FormatDiva) != 0; set => SetFormat(Card.FormatDiva, value); }

    void SetFormat(long bit, bool on)
    {
        long f = on ? CardFormats | bit : CardFormats & ~bit;
        foreach (var p in Prints) p.Formats = f;
        RaiseFormats();
        OnChanged();
    }

    void RaiseFormats()
    {
        Raise(nameof(FormatKey));
        Raise(nameof(FormatDiva));
    }

    /// <summary>Signi classes and lrig types share the class column; the kind says which one it is.</summary>
    bool IsLrig => Kinds.Any(k => k.IsChecked && CardLists.LrigKinds.Contains(k.Text));
    public string ClassLabel => IsLrig ? "루리그 타입" : "클래스";
    public List<string> ClassChoices => IsLrig ? Lists.LrigType : Lists.Class;

    public bool IsOfficial => source == Card.Official;
    public string SourceText => IsOfficial ? "공식" : "오리카";
    // The ja text of an official card, read-only.
    public bool HasJa => !ja.IsEmpty;
    public string JaName => ja.Name;
    public string JaReading => ja.Reading;
    public string JaAbility => ja.Ability;
    public string JaLbText => ja.LifeBurst;
    public string JaFlavor => ja.Flavor;

    public PrintRow? SelectedPrint { get => selectedPrint; set => Set(ref selectedPrint, value); }
    /// <summary>idx of the shown picture: the chosen print, else 1.</summary>
    public int PictureIdx => selectedPrint?.Idx ?? 1;

    public bool IsDirty { get => isDirty; private set => Set(ref isDirty, value); }
    public bool IsNew => Original == null;
    /// <summary>Changes, or a new card with something typed in, that would be lost by showing another card.</summary>
    public bool HasUnsavedWork => IsDirty || IsNew && (name.Trim().Length > 0 || ability.Trim().Length > 0);
    #endregion

    #region which fields a kind uses
    // Taken from what the official cards fill in. A field that has a value is shown whatever the kind,
    // and every field is shown while no kind is chosen.
    static readonly string[] LevelKinds = { "シグニ", "ルリグ", "アシストルリグ", "レゾナ" };
    static readonly string[] LimitKinds = { "ルリグ", "アシストルリグ" };
    static readonly string[] PowerKinds = { "シグニ", "レゾナ" };
    static readonly string[] CostKinds = { "アーツ", "スペル", "ピース", "キー" };
    static readonly string[] GrowKinds = { "ルリグ", "アシストルリグ" };
    static readonly string[] ClassKinds = { "シグニ", "ルリグ", "アシストルリグ", "レゾナ" };
    static readonly string[] TeamKinds = { "ルリグ", "アシストルリグ" };
    static readonly string[] CoinKinds = { "ルリグ" };
    static readonly string[] GuardKinds = { "シグニ" };
    static readonly string[] LbKinds = { "シグニ", "スペル" };
    static readonly string[] TimingKinds = { "アーツ", "アシストルリグ", "ピース" };
    static readonly string[] LimitCondKinds = { "シグニ", "アーツ", "スペル", "キー", "レゾナ" };

    bool Uses(string[] kinds, bool hasValue) =>
        hasValue || Kinds.All(k => !k.IsChecked) || Kinds.Any(k => k.IsChecked && kinds.Contains(k.Text));

    public bool ShowLevel => Uses(LevelKinds, level.Trim().Length > 0);
    public bool ShowLimit => Uses(LimitKinds, limit.Trim().Length > 0);
    public bool ShowPower => Uses(PowerKinds, power.Trim().Length > 0);
    public bool ShowCost => Uses(CostKinds, costEna.Trim().Length > 0 || costOther.Trim().Length > 0);
    public bool ShowGrow => Uses(GrowKinds, growEna.Trim().Length > 0 || growOther.Trim().Length > 0);
    public bool ShowClass => Uses(ClassKinds, classes.Trim().Length > 0);
    public bool ShowTeam => Uses(TeamKinds, team.Trim().Length > 0);
    public bool ShowCoin => Uses(CoinKinds, coin.Trim().Length > 0);
    public bool ShowGuard => Uses(GuardKinds, guard);
    public bool ShowLifeBurst => Uses(LbKinds, lifeBurst || lbText.Trim().Length > 0);
    public bool ShowTiming => Uses(TimingKinds, Timings.Any(t => t.IsChecked));
    public bool ShowLimitCond => Uses(LimitCondKinds, limitCond.Trim().Length > 0);
    public bool ShowCostRow => ShowCost || ShowGrow;
    public bool ShowOtherRow => ShowTiming || ShowGuard || ShowLifeBurst;

    static readonly string[] ShowNames =
    {
        nameof(ShowLevel), nameof(ShowLimit), nameof(ShowPower), nameof(ShowCost), nameof(ShowGrow), nameof(ShowClass),
        nameof(ShowTeam), nameof(ShowCoin), nameof(ShowGuard), nameof(ShowLifeBurst), nameof(ShowTiming), nameof(ShowLimitCond),
        nameof(ShowCostRow), nameof(ShowOtherRow),
    };
    #endregion

    void OnKindChanged(FlagOption _)
    {
        foreach (var n in ShowNames) Raise(n);
        Raise(nameof(ClassLabel));
        Raise(nameof(ClassChoices));
        OnChanged();
    }

    void OnColorChanged(FlagOption o)
    {
        // 無 and the colors exclude each other.
        if (!loading && o.IsChecked)
            foreach (var c in Colors)
                if (c != o && (o.Value == Colorless || c.Value == Colorless)) c.SetSilently(false);
        OnChanged();
    }

    void OnChanged()
    {
        if (loading) return;
        IsDirty = !ToCard().SameAs(baseline);
        Changed?.Invoke();
    }

    /// <summary>Shows a card. Pass a card that is in the database as fromDb so saving knows to update it.</summary>
    public void Load(Card? c, bool fromDb = true)
    {
        loading = true;
        Original = c != null && fromDb ? c.Clone() : null;
        c ??= new Card();
        loaded = c.Clone();
        source = c.Source.Length > 0 ? c.Source : Card.Orica;
        Code = c.Id == 0 ? "" : c.Id.ToString();
        Alias = c.Alias == 0 ? "" : c.Alias.ToString();
        Name = c.Ko.Name; Reading = c.Ko.Reading; Ability = c.Ko.Ability; LbText = c.Ko.LifeBurst; Flavor = c.Ko.Flavor;
        ja = c.Ja.Clone();

        foreach (var k in c.Kind) AddChip(Kinds, k, OnKindChanged);
        foreach (var k in Kinds) k.SetSilently(c.Kind.Contains(k.Text));
        foreach (var t in c.Timing) AddChip(Timings, t, _ => OnChanged());
        foreach (var t in Timings) t.SetSilently(c.Timing.Contains(t.Text));
        foreach (var col in Colors)
            col.SetSilently(c.Color != null && (col.Value == Colorless ? c.Color == 0 : (c.Color & col.Value) != 0));

        Level = Card.NumberText(c.Level); Limit = Card.NumberText(c.Limit); Power = Card.NumberText(c.Power); Coin = Card.NumberText(c.Coin);
        CostEna = c.CostEna ?? ""; CostOther = c.CostOther; GrowEna = c.GrowEna ?? ""; GrowOther = c.GrowOther;
        Classes = JoinList(c.Class); LimitCond = JoinList(c.LimitCond); Team = JoinList(c.Team);
        Guard = c.Guard; LifeBurst = c.LifeBurst;
        Maker = c.Maker; Tags = c.Tags; Pack = c.Pack; Created = c.Created; Modified = c.Modified;

        Prints.Clear();
        foreach (var p in c.Prints) Prints.Add(new PrintRow(p, OnChanged));
        SelectedPrint = Prints.FirstOrDefault();

        foreach (var n in new[] { nameof(IsOfficial), nameof(SourceText), nameof(HasJa), nameof(JaName), nameof(JaReading),
                                  nameof(JaAbility), nameof(JaLbText), nameof(JaFlavor), nameof(ClassLabel), nameof(ClassChoices),
                                  nameof(FormatKey), nameof(FormatDiva) }.Concat(ShowNames))
            Raise(n);
        loading = false;
        baseline = ToCard();
        IsDirty = false;
        Raise(nameof(IsNew));
        Changed?.Invoke();
    }

    /// <summary>Marks the current contents as saved (after writing them to the database).</summary>
    public void MarkSaved(Card saved)
    {
        Original = saved.Clone();
        loading = true;
        Created = saved.Created;
        Modified = saved.Modified;
        loading = false;
        baseline = ToCard();
        IsDirty = false;
        Raise(nameof(IsNew));
    }

    #region list fields and prints
    public const string ListSeparator = ", ";
    public static string JoinList(IEnumerable<string> list) => string.Join(ListSeparator, list);
    public static List<string> SplitList(string s) =>
        s.Split(new[] { ',', '、' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();

    /// <summary>Adds a choice from the drop-down to a list field ("Classes", "LimitCond" or "Team").</summary>
    /// <summary>A symbol button: the icon code (《白》 and so on) goes at the end of the ability text.</summary>
    public void AppendSymbol(string code) => Ability += code;

    public void Append(string field, string value)
    {
        value = value.Trim();
        if (value.Length == 0) return;
        string now = field switch { nameof(Classes) => classes, nameof(LimitCond) => limitCond, _ => team };
        var list = SplitList(now);
        if (list.Contains(value)) return;
        list.Add(value);
        string joined = JoinList(list);
        if (field == nameof(Classes)) Classes = joined;
        else if (field == nameof(LimitCond)) LimitCond = joined;
        else Team = joined;
    }

    public PrintRow AddPrint()
    {
        var first = Prints.FirstOrDefault();
        var row = new PrintRow(new CardPrint { Idx = Prints.Count + 1, SetCode = first?.SetCode ?? "", Formats = CardFormats }, OnChanged);
        Prints.Add(row);
        SelectedPrint = row;
        return row;
    }

    public void RemovePrint(PrintRow row)
    {
        int at = Prints.IndexOf(row);
        if (at < 0) return;
        Prints.RemoveAt(at);
        SelectedPrint = Prints.Count == 0 ? null : Prints[Math.Min(at, Prints.Count - 1)];
    }

    public void MovePrint(PrintRow row, int by)
    {
        int at = Prints.IndexOf(row), to = at + by;
        if (at < 0 || to < 0 || to >= Prints.Count) return;
        Prints.Move(at, to);
        SelectedPrint = row;
    }

    /// <summary>idx follows the order of the list: the first row is idx 1, the main illustration.</summary>
    void Renumber()
    {
        bool was = loading;
        loading = true;
        for (int i = 0; i < Prints.Count; i++) Prints[i].Idx = i + 1;
        loading = was;
        Raise(nameof(PictureIdx));
    }
    #endregion

    #region numbers and checks
    /// <summary>"" → null, "∞" or "-1" → -1, else the number.</summary>
    public static bool TryNumber(string s, out long? v)
    {
        s = s.Trim();
        v = null;
        if (s.Length == 0) return true;
        if (s == "∞" || s == "-1") { v = Card.Infinite; return true; }
        if (long.TryParse(s, out long n) && n >= 0) { v = n; return true; }
        return false;
    }

    static long? Num(string s) => TryNumber(s, out var v) ? v : null;

    public const string DateFormat = "yyyy-MM-dd";
    static bool IsDate(string s) => s.Trim().Length == 0 ||
        DateTime.TryParseExact(s.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    /// <summary>An empty box keeps what the card had (null = no cost, "" = a cost of nothing); a new card gets null.</summary>
    static string? Cost(string typed, string? was) => typed.Trim().Length > 0 ? typed.Trim() : was == "" ? "" : null;

    /// <summary>Checked chips in the order the loaded card had them, new ones after.</summary>
    static List<string> Ordered(IEnumerable<FlagOption> chips, List<string> was) =>
        chips.Where(c => c.IsChecked).Select(c => c.Text)
             .OrderBy(t => was.IndexOf(t) is int i && i >= 0 ? i : int.MaxValue).ToList();

    static bool IsCost(string s) => System.Text.RegularExpressions.Regex.IsMatch(s.Trim(), @"^(\{[A-Z0-9/]+\})*$");

    static bool IsJson(string s)
    {
        if (s.Trim().Length == 0) return true;
        try { System.Text.Json.JsonDocument.Parse(s).Dispose(); return true; }
        catch (System.Text.Json.JsonException) { return false; }
    }

    /// <summary>What is wrong with the typed values, or null when they can be saved.</summary>
    public string? Validate()
    {
        if (!long.TryParse(code.Trim(), out long id) || id <= 0) return "카드 코드(cid)를 숫자로 입력해 주세요.";
        if (alias.Trim().Length > 0 && !long.TryParse(alias.Trim(), out _)) return "동명 취급은 카드 코드(숫자)여야 합니다.";
        foreach (var (label, v) in new[] { ("레벨", level), ("리미트", limit), ("파워", power), ("코인", coin) })
            if (!TryNumber(v, out _)) return $"{label}은(는) 0 이상의 숫자나 ∞로 입력해 주세요. (비워 둬도 돼요)";
        if (!IsCost(costEna)) return "비용은 {W}{R}{U}{G}{B}{C} 형식으로 입력해 주세요. 혼성은 {W/B}.";
        if (!IsCost(growEna)) return "그로우 비용은 {W}{R}{U}{G}{B}{C} 형식으로 입력해 주세요.";
        if (!IsJson(costOther) || !IsJson(growOther)) return "기타 비용은 {\"coin\": 1} 같은 JSON으로 입력해 주세요. (비워 둬도 돼요)";
        if (!IsDate(created)) return "제작일은 2026-10-30처럼 입력해 주세요. (비워 둬도 돼요)";
        if (!IsDate(modified)) return "수정일은 2026-10-30처럼 입력해 주세요. (비워 둬도 돼요)";
        return null;
    }

    public Card ToCard()
    {
        long? color = null;
        foreach (var c in Colors.Where(c => c.IsChecked))
            color = c.Value == Colorless ? 0 : (color ?? 0) | c.Value;
        return new Card
        {
            Id = long.TryParse(code.Trim(), out long id) ? id : 0,
            Alias = long.TryParse(alias.Trim(), out long a) ? a : 0,
            Source = source,
            Kind = Ordered(Kinds, loaded.Kind),
            Timing = Ordered(Timings, loaded.Timing),
            Color = color,
            Level = Num(level), Limit = Num(limit), Power = Num(power), Coin = Num(coin),
            CostEna = Cost(costEna, loaded.CostEna), CostOther = costOther.Trim(),
            GrowEna = Cost(growEna, loaded.GrowEna), GrowOther = growOther.Trim(),
            Class = SplitList(classes), LimitCond = SplitList(limitCond), Team = SplitList(team),
            Guard = guard, LifeBurst = lifeBurst,
            Ko = new CardText { Name = name.Trim(), Reading = reading.Trim(), Ability = ability, LifeBurst = lbText, Flavor = flavor },
            Ja = ja.Clone(),
            Prints = Prints.Select(p => p.ToPrint()).ToList(),
            Maker = maker.Trim(), Tags = tags.Trim(), Pack = pack.Trim(), Created = created.Trim(), Modified = modified.Trim(),
        };
    }

    /// <summary>
    /// An orica gets today as 제작일 when new, and as 수정일 when changed unless that box was edited by hand.
    /// Official cards get no card_meta.
    /// </summary>
    public Card ToSaveCard(DateTime today)
    {
        var c = ToCard();
        if (c.IsOfficial) return c;
        string d = today.ToString(DateFormat, CultureInfo.InvariantCulture);
        if (IsNew && c.Created.Length == 0) c.Created = d;
        if ((IsNew || IsDirty) && c.Modified == baseline.Modified) c.Modified = d;
        return c;
    }
    #endregion
}
