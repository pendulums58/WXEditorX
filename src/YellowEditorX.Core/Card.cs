namespace YellowEditorX.Core;

/// <summary>One language row of texts. Official cards have ja; an orica only ko.</summary>
public sealed class CardText
{
    public string Name = "", Reading = "", Ability = "", LifeBurst = "", Flavor = "";

    public bool IsEmpty => Name.Length == 0 && Reading.Length == 0 && Ability.Length == 0 && LifeBurst.Length == 0 && Flavor.Length == 0;
    public CardText Clone() => (CardText)MemberwiseClone();
    public bool SameAs(CardText o) =>
        Name == o.Name && Reading == o.Reading && Ability == o.Ability && LifeBurst == o.LifeBurst && Flavor == o.Flavor;
}

/// <summary>One row of prints: an illustration of the card. idx 1 is the main one.</summary>
public sealed class CardPrint
{
    public int Idx = 1;
    public string CardNo = "", SetCode = "", Rarity = "", Illustrator = "", Image = "", RawName = "";
    public long? Formats;

    public CardPrint Clone() => (CardPrint)MemberwiseClone();
    public bool SameAs(CardPrint o) =>
        Idx == o.Idx && CardNo == o.CardNo && SetCode == o.SetCode && Rarity == o.Rarity && Illustrator == o.Illustrator
        && Image == o.Image && RawName == o.RawName && Formats == o.Formats;
}

/// <summary>
/// One card of a pack .db: its cards row with the texts (per language), prints and card_meta rows.
/// List columns (kind, class, ...) are JSON arrays in the database and lists here.
/// </summary>
public sealed class Card
{
    public const string Official = "official", Orica = "orica";
    /// <summary>Color bits. 0 = 무색, null = no color at all.</summary>
    public const int White = 1, Red = 2, Blue = 4, Green = 8, Black = 16;
    /// <summary>Level, limit and power use this for ∞.</summary>
    public const long Infinite = -1;

    public long Id;
    public List<string> Kind = new(), Class = new(), LimitCond = new(), Team = new(), Timing = new();
    public long? Color, Level, Limit, Power, Coin;
    /// <summary>Ena costs like "{R}{C}{C}". null (no cost) and "" (a cost of nothing) are both in the official data.</summary>
    public string? CostEna, GrowEna;
    public string CostOther = "", GrowOther = "";
    public bool Guard, LifeBurst;
    public string Source = Orica;
    public long Alias;

    public CardText Ko = new(), Ja = new();
    public List<CardPrint> Prints = new();
    // card_meta
    public string Maker = "", Tags = "", Pack = "";
    /// <summary>yyyy-MM-dd or empty.</summary>
    public string Created = "", Modified = "";

    /// <summary>The name shown in lists: ko, or ja for an official card not translated yet.</summary>
    public string Name => Ko.Name.Length > 0 ? Ko.Name : Ja.Name;
    public bool IsOfficial => Source == Official;
    public bool HasMeta => Maker.Length > 0 || Tags.Length > 0 || Pack.Length > 0 || Created.Length > 0 || Modified.Length > 0;

    public Card Clone()
    {
        var c = (Card)MemberwiseClone();
        c.Kind = new(Kind); c.Class = new(Class); c.LimitCond = new(LimitCond); c.Team = new(Team); c.Timing = new(Timing);
        c.Ko = Ko.Clone(); c.Ja = Ja.Clone();
        c.Prints = Prints.Select(p => p.Clone()).ToList();
        return c;
    }

    public bool SameAs(Card? o)
    {
        if (o is null) return false;
        return Id == o.Id && Kind.SequenceEqual(o.Kind) && Class.SequenceEqual(o.Class) && LimitCond.SequenceEqual(o.LimitCond)
            && Team.SequenceEqual(o.Team) && Timing.SequenceEqual(o.Timing)
            && Color == o.Color && Level == o.Level && Limit == o.Limit && Power == o.Power && Coin == o.Coin
            && CostEna == o.CostEna && CostOther == o.CostOther && GrowEna == o.GrowEna && GrowOther == o.GrowOther
            && Guard == o.Guard && LifeBurst == o.LifeBurst && Source == o.Source && Alias == o.Alias
            && Ko.SameAs(o.Ko) && Ja.SameAs(o.Ja)
            && Prints.Count == o.Prints.Count && Prints.Zip(o.Prints).All(p => p.First.SameAs(p.Second))
            && Maker == o.Maker && Tags == o.Tags && Pack == o.Pack && Created == o.Created && Modified == o.Modified;
    }

    public static readonly (int Bit, string Text)[] Colors = { (White, "白"), (Red, "赤"), (Blue, "青"), (Green, "緑"), (Black, "黒") };

    public static string ColorText(long? color)
    {
        if (color == null) return "";
        if (color == 0) return "無";
        return string.Concat(Colors.Where(c => (color & c.Bit) != 0).Select(c => c.Text));
    }

    public static string NumberText(long? v) => v == null ? "" : v == Infinite ? "∞" : v.Value.ToString();
}
