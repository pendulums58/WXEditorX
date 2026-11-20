using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using YellowEditorX.Core;

namespace YellowEditorX;

/// <summary>
/// A scripted run through the real window: rows are selected, text is typed into the actual text boxes,
/// buttons are clicked and questions are answered from a queue. Only ever run on copied data (--selftest).
/// </summary>
public partial class MainWindow
{
    readonly StringBuilder testLog = new();
    int testFailures;

    void T(bool ok, string what)
    {
        if (!ok) testFailures++;
        testLog.AppendLine((ok ? "ok   " : "FAIL ") + what);
    }

    static Task Idle() => Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;

    static IEnumerable<T2> Children<T2>(DependencyObject root) where T2 : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T2 t) yield return t;
            foreach (var d in Children<T2>(c)) yield return d;
        }
    }

    ToggleButton Chip(string text) => Children<ToggleButton>(Editor).First(b => (b.Content as string) == text);
    static void Press(Button b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    async Task Select(int index)
    {
        CardList.SelectedItem = shownRows[index];
        await Idle();
    }

    void Answer(params MessageBoxResult[] answers)
    {
        testAnswers!.Clear();
        foreach (var a in answers) testAnswers.Enqueue(a);
        testPrompts.Clear();
    }

    void Snapshot(string dir, string name)
    {
        UpdateLayout();
        var rtb = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(this);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(Path.Combine(dir, name));
        enc.Save(fs);
    }

    /// <summary>
    /// outDir gets the report's screenshots. The workspace copy must hold a copy of the official wixoss.db;
    /// a new pack is made next to it.
    /// </summary>
    public async Task<string> RunSelfTest(string outDir)
    {
        WindowState = WindowState.Normal;
        Width = 1920; Height = 1050;
        await Idle();

        // 1. the official DB: ja text read-only, prints listed
        var official = dbs.First(d => CardDb.ReadAll(d.Path).Any(c => c.IsOfficial));
        DbList.SelectedItem = official;
        await Idle();
        T(allRows.Count > 7000, $"official cards listed ({allRows.Count})");
        await Select(0);
        var first = currentRow!.Card;
        T(model.HasJa && model.JaName == first.Ja.Name && model.IsOfficial && model.Prints.Count == first.Prints.Count && model.Prints.Count > 0,
          $"official card shown: {model.JaName}, prints {model.Prints.Count}");
        T(!model.IsDirty, "showing a card changes nothing");
        T(model.ShowLimit && !model.ShowPower, "ルリグ: limit shown, power hidden");
        Snapshot(outDir, "official.png");

        // 2. a ko translation of an official card: only texts(ko) is added, ja and card_meta stay as they were
        string oldKo = first.Ko.Name;
        NameBox.Text = "테스트 번역";
        await Idle();
        T(model.IsDirty, "typing makes the card unsaved");
        T(SaveCurrent(), "official card saved");
        var back = CardDb.Read(official.Path, first.Id)!;
        T(back.Ko.Name == "테스트 번역" && back.Ja.SameAs(first.Ja) && !back.HasMeta && back.Prints.Count == first.Prints.Count
          && back.Kind.SequenceEqual(first.Kind) && back.Color == first.Color && back.Level == first.Level,
          "ko saved, ja / prints / card_meta untouched");
        Undo();
        T(CardDb.Read(official.Path, first.Id)!.Ko.Name == oldKo, "undo puts the old ko text back");

        // 3. a new pack with the build_db.py tables
        string packDir = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(official.Path))!, "selftest_pack");
        if (Directory.Exists(packDir)) Directory.Delete(packDir, true);
        string pack = Path.Combine(packDir, "TestPack.db");
        CardDb.Create(pack);
        T(CardDb.IsPack(pack), "new pack has the four tables");
        T(AddDb(pack), "new pack opened");
        RememberDbView();
        RefreshDbList(pack);
        await Idle();
        T(currentDb != null && SamePath(currentDb.Path, pack) && allRows.Count == 0, "new pack is empty");

        // 4. a new card: next free code after the official block, fields by kind
        settings.LastMaker = "시안";
        NewCard();
        await Idle();
        T(model.Code == "1000001", $"first code of the new pack: {model.Code}");
        T(model.Maker == "시안" && model.Prints.Count == 1, "maker filled in, one print");
        T(model.ShowPower && !model.ShowLimit && !model.ShowGrow, "シグニ: power shown, limit and grow hidden");
        NameBox.Text = "테스트 시그니";
        model.Level = "3";
        model.Power = "8000";
        Chip("赤").IsChecked = true;
        Chip("青").IsChecked = true;
        model.Append(nameof(EditorModel.Classes), "精武：アーム");
        model.Append(nameof(EditorModel.Classes), "精武：ウェポン");
        model.LifeBurst = true;
        model.LbText = "카드를 1장 뽑는다.";
        model.Ability = "【常】：이 시그니의 파워는 +1000된다.";
        model.Prints[0].CardNo = "TEST-001";
        model.Prints[0].Rarity = "SR";
        await Idle();
        Chip("無").IsChecked = true;
        T(Chip("赤").IsChecked == false && Chip("青").IsChecked == false, "無 turns the colors off");
        Chip("赤").IsChecked = true;
        Chip("青").IsChecked = true;
        T(Chip("無").IsChecked == false, "a color turns 無 off");
        T(SaveCurrent(), "new card saved");
        var saved = CardDb.Read(pack, 1000001)!;
        string today = DateTime.Today.ToString(EditorModel.DateFormat);
        T(saved.Ko.Name == "테스트 시그니" && saved.Ja.IsEmpty && saved.Source == Card.Orica && saved.Kind.SequenceEqual(new[] { "シグニ" })
          && saved.Color == (Card.Red | Card.Blue) && saved.Level == 3 && saved.Power == 8000 && saved.Limit == null
          && saved.Class.SequenceEqual(new[] { "精武：アーム", "精武：ウェポン" }) && saved.LifeBurst && saved.Ko.LifeBurst == "카드를 1장 뽑는다."
          && saved.Prints.Count == 1 && saved.Prints[0].CardNo == "TEST-001" && saved.Prints[0].Rarity == "SR"
          && saved.Maker == "시안" && saved.Created == today && saved.Modified == today,
          "new card read back field by field");
        T(!model.IsDirty && currentRow?.Card.Id == 1000001, "saved card is the current row");

        // 5. images: idx 1 is pics\<cid>.png, idx 2 pics\<cid>_2.png
        var bmp = BitmapSource.Create(4, 6, 96, 96, PixelFormats.Bgr32, null, new byte[4 * 6 * 4], 4 * 4);
        ImportImage(bmp, "test");
        var files = new CardFiles(pack);
        T(File.Exists(files.Picture(1000001)) && CardImage.Source != null, "picture of idx 1 written and shown");
        model.AddPrint();
        model.Prints[1].CardNo = "TEST-001P";
        await Idle();
        T(model.PictureIdx == 2 && CardImage.Source == null, "new print selected, no picture yet");
        ImportImage(bmp, "test");
        T(File.Exists(files.Picture(1000001, 2)), "picture of idx 2 written as _2");
        T(SaveCurrent() && CardDb.Read(pack, 1000001)!.Prints.Select(p => p.Idx).SequenceEqual(new[] { 1, 2 }), "two prints saved");
        model.MovePrint(model.Prints[1], -1);
        T(model.Prints[0].CardNo == "TEST-001P" && model.Prints[0].Idx == 1 && model.Prints[1].Idx == 2, "moving a print renumbers idx");
        model.Load(CardDb.Read(pack, 1000001));

        // 6. checks before saving
        model.Power = "abc";
        Answer();
        T(!SaveCurrent() && testPrompts.Any(p => p.Contains("파워")), "a bad power is refused");
        model.Power = "∞";
        T(model.ToCard().Power == Card.Infinite, "∞ is -1");
        model.Power = "8000";
        model.CostEna = "{R}{X";
        Answer();
        T(!SaveCurrent() && testPrompts.Any(p => p.Contains("비용")), "a bad cost is refused");
        model.CostEna = "";
        T(!model.IsDirty, "back to the saved values");

        // 7. duplicate copies the pictures, delete takes them away, undo brings them back
        Duplicate();
        await Idle();
        T(model.Code == "1000002" && model.IsNew, $"duplicate gets the next code: {model.Code}");
        T(SaveCurrent(), "duplicate saved");
        T(File.Exists(files.Picture(1000002)) && File.Exists(files.Picture(1000002, 2)), "duplicate got both pictures");
        CardList.SelectedItems.Clear();
        CardList.SelectedItem = shownRows.First(r => r.Card.Id == 1000002);
        await Idle();
        Answer(MessageBoxResult.OK);
        DeleteSelected();
        T(!CardDb.ReadIds(pack).Contains(1000002) && !File.Exists(files.Picture(1000002)), "deleted with its pictures");
        Undo();
        T(CardDb.ReadIds(pack).Contains(1000002) && File.Exists(files.Picture(1000002, 2)), "undo brings the card and pictures back");

        // 8. the script is named after the cid
        var lua = files.EnsureScript(CardDb.Read(pack, 1000002)!, out bool made);
        T(made && Path.GetFileName(lua) == "c1000002.lua" && File.ReadAllText(lua).StartsWith("--테스트 시그니"), "script made from the template");

        Snapshot(outDir, "orica.png");

        // 9. copying text goes through ClipboardText (a busy clipboard is retried)
        bool busy = true;
        ClipboardText.TestWrite = () => !busy;
        ClipboardText.SetText("【出】カード");
        T(ClipboardText.GetText() == "【出】カード" && ClipboardText.TestWrites == 0, "copied text kept while the clipboard is busy");
        busy = false;
        await Task.Delay(100);
        T(ClipboardText.TestWrites == 1, "written once the clipboard is free");
        ClipboardText.TestWrite = null;

        // 10. the ▾ menu shows which program opens scripts
        var editorBefore = settings.ScriptEditor;
        settings.ScriptEditor = null;
        ScriptMenuButton_Click(this, new RoutedEventArgs());
        T(ScriptMenu.IsOpen && (string)ScriptEditorItem.Header == "지금: Windows 기본 프로그램" && !ResetScriptEditorItem.IsEnabled,
            $"script menu, default: {ScriptEditorItem.Header}");
        ScriptMenu.IsOpen = false;
        settings.ScriptEditor = editorBefore;

        model.Load(null);
        testLog.AppendLine(testFailures == 0 ? "SELFTEST ALL PASS" : $"{testFailures} SELFTEST FAILURES");
        return testLog.ToString();
    }
}
