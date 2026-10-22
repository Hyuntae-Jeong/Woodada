using Woodada.Core;
using Woodada.Interop;
using Woodada.Rewards;
using Woodada.Settings;
using Woodada.Sprites;
using Woodada.Stats;

namespace Woodada.UI;

/// <summary>
/// 맨 위: 가진 생선 · 다음 생선까지 진행 막대.
/// 고양이 — CPU·메모리 고양이 카드(표시 · 미리보기 · 크기)와 색 칸 20개(기본 10 + 나만의 색 10).
/// 옷장 — 러너 모음. 기록 — 달린 거리. 일반 — 모니터 · 전체 화면.
/// 바꾸는 즉시 오버레이에 반영하고 저장한다.
/// </summary>
internal sealed class SettingsForm : Form
{
    public enum Page { Cats, Closet, Profile, General }

    private readonly SettingsStore _store;
    private readonly StatsStore _stats;
    private readonly RewardStore _rewards;
    private readonly MetricSampler _sampler;
    private AppSettings _working;
    private string _selectedId = "cpu";
    private bool _loading;
    private long _lastFish = -1;

    private readonly WalletHeader _header = new();
    private readonly Dictionary<Page, (NavPill Pill, Control View)> _pages = new();
    private readonly Panel _content = new();
    private Page _page = Page.Cats;
    private readonly Button _reset = new();
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 1000 };

    // 고양이 탭
    private readonly FlowLayoutPanel _cards = new();
    private readonly List<RunnerCard> _runnerCards = new();
    private readonly List<(PresetColor Color, ShopTile Tile)> _presetTiles = new();
    private readonly List<ShopTile> _customTiles = new();
    private readonly Label _catsHint = new();
    private readonly FlowLayoutPanel _trialButtons = new();
    private readonly Button _trialBuy = new();
    private readonly Button _trialRepick = new();
    private readonly Button _trialCancel = new();
    private Trial? _trial;

    /// <summary>사기 전에 입어 보는 색. Preset이 없으면 나만의 색 칸(CustomIndex)이다.</summary>
    private sealed record Trial(string Hex, string Label, int Price, PresetColor? Preset, int CustomIndex);

    // 옷장 탭
    private readonly FlowLayoutPanel _runnerTiles = new();
    private readonly Label _closetHint = new();

    // 기록 탭
    private readonly Label _todayHeadline = new();
    private readonly Label _todayCompare = new();
    private readonly TableLayoutPanel _runnerStats = new();
    private readonly Dictionary<string, (Label Today, Label Total)> _runnerStatLabels = new();
    private readonly Label _totalLine = new();
    private readonly Label _totalCompare = new();
    private readonly Label _fishLine = new();
    private readonly Label _pitchNote = new();

    // 일반 탭
    private readonly ComboBox _monitor = new();
    private readonly CheckBox _hideFullScreen = new();

    public SettingsForm(SettingsStore store, StatsStore stats, RewardStore rewards, MetricSampler sampler)
    {
        _store = store;
        _stats = stats;
        _rewards = rewards;
        _sampler = sampler;
        _working = store.Current.Clone();
        _selectedId = _working.Runners.FirstOrDefault()?.Id ?? "cpu";

        Text = AppInfo.IsDev ? "Woodada (개발용)" : "Woodada";
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        Font = new Font("Segoe UI", 9f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = GameArt.Background;
        ForeColor = GameArt.Text;
        ClientSize = new Size(700, 690);

        BuildLayout();
        LoadAll();
        RefreshWallet();
        ShowPage(Page.Cats);

        // 트레이 메뉴에서 표시 여부를 바꿔도 열려 있는 설정 창이 따라가게 한다.
        _store.Changed += OnStoreChanged;
        _rewards.Changed += OnRewardsChanged;
        // 창이 열려 있는 동안 고양이가 달리는 만큼 생선과 기록이 올라가게 한다.
        _tick.Tick += (_, _) =>
        {
            RefreshWallet();
            if (_page == Page.Profile) RefreshProfile();
        };
        _tick.Start();
        FormClosed += (_, _) =>
        {
            _store.Changed -= OnStoreChanged;
            _rewards.Changed -= OnRewardsChanged;
            _tick.Dispose();
        };
    }

    public void ShowPage(Page page)
    {
        if (page != Page.Cats) EndTrial();
        _page = page;
        foreach (var (p, (pill, view)) in _pages)
        {
            pill.Selected = p == page;
            view.Visible = p == page;
        }
        _reset.Visible = page == Page.Cats;
        if (page == Page.Profile)
        {
            BuildRunnerStatRows();
            RefreshProfile();
        }
        if (page == Page.Closet) RefreshCloset();
    }

    private RunnerConfig? Selected => _working.Runners.FirstOrDefault(r => r.Id == _selectedId) ?? _working.Runners.FirstOrDefault();

    // ───────────────────────── 레이아웃 ─────────────────────────

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(0), BackColor = GameArt.Background };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        _header.Dock = DockStyle.Fill;
        _header.Margin = new Padding(0);
        root.Controls.Add(_header, 0, 0);

        var nav = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(12, 10, 12, 4), BackColor = GameArt.Background };
        root.Controls.Add(nav, 0, 1);

        _content.Dock = DockStyle.Fill;
        _content.Margin = new Padding(12, 4, 12, 0);
        _content.BackColor = GameArt.Background;
        root.Controls.Add(_content, 0, 2);

        AddPage(nav, Page.Cats, "고양이", BuildCatsPage());
        AddPage(nav, Page.Closet, "옷장", BuildClosetPage());
        AddPage(nav, Page.Profile, "기록", BuildProfilePage());
        AddPage(nav, Page.General, "일반", BuildGeneralPage());

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(12, 6, 12, 12), BackColor = GameArt.Background,
        };
        // 모달이 아닌 창(Show)이라 DialogResult만으로는 닫히지 않는다. 직접 Close 한다.
        var close = new Button { Text = "닫기", AutoSize = true, MinimumSize = new Size(80, 0), BackColor = GameArt.Card };
        close.Click += (_, _) => Close();
        buttons.Controls.Add(close);
        _reset.Text = "선택한 고양이 기본값으로";
        _reset.AutoSize = true;
        _reset.BackColor = GameArt.Card;
        _reset.Click += (_, _) => ResetSelected();
        buttons.Controls.Add(_reset);
        AcceptButton = close;
        CancelButton = close;
        root.Controls.Add(buttons, 0, 3);
    }

    private void AddPage(FlowLayoutPanel nav, Page page, string title, Control view)
    {
        var pill = new NavPill(title);
        pill.Click += (_, _) => ShowPage(page);
        nav.Controls.Add(pill);
        view.Dock = DockStyle.Fill;
        view.Visible = false;
        _content.Controls.Add(view);
        _pages[page] = (pill, view);
    }

    private static Label SectionTitle(string text) => new()
    {
        Text = text, AutoSize = true, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), ForeColor = GameArt.Text,
        Margin = new Padding(0, 12, 8, 4),
    };

    private static Label SectionNote(string text) => new()
    {
        Text = text, AutoSize = true, ForeColor = GameArt.SubText, Margin = new Padding(0, 15, 0, 4),
    };

    private static Control SectionHeader(string title, string note)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), BackColor = GameArt.Background };
        row.Controls.Add(SectionTitle(title));
        row.Controls.Add(SectionNote(note));
        return row;
    }

    private static Label HintBox(Label label)
    {
        label.Dock = DockStyle.Fill;
        label.AutoSize = false;
        label.Height = 36;
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.Padding = new Padding(12, 0, 12, 0);
        label.Margin = new Padding(0, 10, 0, 0);
        label.BackColor = GameArt.AccentSoft;
        label.ForeColor = GameArt.Text;
        return label;
    }

    // ───────────────────────── 고양이 탭 ─────────────────────────

    private Control BuildCatsPage()
    {
        var page = new TableLayoutPanel { ColumnCount = 1, BackColor = GameArt.Background, Padding = new Padding(0, 6, 0, 0) };
        for (int i = 0; i < 6; i++) page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _cards.AutoSize = true;
        _cards.WrapContents = false;
        _cards.Margin = new Padding(0);
        _cards.BackColor = GameArt.Background;
        page.Controls.Add(_cards);

        page.Controls.Add(SectionHeader("기본 색", "까망·하양은 처음부터 있어요. 나머지는 생선으로 열어요"));
        var presets = TileRow();
        foreach (var color in RewardCatalog.Presets)
        {
            var tile = ColorTile(color.Name, color.Hex);
            tile.Price = color.Price;
            var c = color;
            tile.Click += (_, _) => OnPresetClicked(c, tile);
            tile.MouseEnter += (_, _) => ShowHint(PresetHint(c));
            tile.MouseLeave += (_, _) => ShowHint(DefaultCatsHint());
            presets.Controls.Add(tile);
            _presetTiles.Add((color, tile));
        }
        _presetTiles[^1].Tile.Margin = new Padding(0);
        page.Controls.Add(presets);

        page.Controls.Add(SectionHeader("나만의 색", "직접 고른 색을 담아요. 앞 칸부터 열리고, 10칸을 다 열면 색을 바꿀 수 있어요"));
        var customs = TileRow();
        for (int i = 0; i < RewardCatalog.CustomSlotCount; i++)
        {
            int index = i;
            var tile = ColorTile($"{i + 1}번 칸", null);
            tile.Price = RewardCatalog.CustomSlotPrice(i);
            tile.MouseUp += (_, e) => OnCustomClicked(index, tile, e.Button);
            tile.MouseEnter += (_, _) => ShowHint(CustomHint(index));
            tile.MouseLeave += (_, _) => ShowHint(DefaultCatsHint());
            customs.Controls.Add(tile);
            _customTiles.Add(tile);
        }
        _customTiles[^1].Margin = new Padding(0);
        page.Controls.Add(customs);

        page.Controls.Add(BuildCatsBar());
        page.Controls.Add(new Panel { Dock = DockStyle.Fill }); // 남는 높이는 여기로. 없으면 마지막 줄(안내 상자)이 늘어난다
        return page;
    }

    /// <summary>색 칸 아래 안내 상자. 입어 보는 중에는 오른쪽에 사기 · 다른 색 · 그만 버튼이 붙는다.</summary>
    private Control BuildCatsBar()
    {
        var bar = new TableLayoutPanel
        {
            ColumnCount = 2, RowCount = 1, Anchor = AnchorStyles.Left | AnchorStyles.Right, Height = 40,
            Margin = new Padding(0, 10, 0, 0), BackColor = GameArt.AccentSoft,
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        HintBox(_catsHint);
        _catsHint.Margin = new Padding(0);
        bar.Controls.Add(_catsHint, 0, 0);

        _trialButtons.AutoSize = true;
        _trialButtons.WrapContents = false;
        _trialButtons.Anchor = AnchorStyles.Right;
        _trialButtons.Margin = new Padding(0, 0, 6, 0);
        _trialButtons.BackColor = GameArt.AccentSoft;
        _trialButtons.Visible = false;
        StyleBarButton(_trialBuy, primary: true);
        StyleBarButton(_trialRepick, primary: false);
        StyleBarButton(_trialCancel, primary: false);
        _trialRepick.Text = "다른 색";
        _trialCancel.Text = "그만";
        _trialBuy.Click += (_, _) => BuyTrial();
        _trialRepick.Click += (_, _) => RepickTrial();
        _trialCancel.Click += (_, _) => EndTrial();
        _trialButtons.Controls.Add(_trialBuy);
        _trialButtons.Controls.Add(_trialRepick);
        _trialButtons.Controls.Add(_trialCancel);
        bar.Controls.Add(_trialButtons, 1, 0);
        return bar;
    }

    private static void StyleBarButton(Button b, bool primary)
    {
        b.AutoSize = true;
        b.FlatStyle = FlatStyle.Flat;
        b.Cursor = Cursors.Hand;
        b.Margin = new Padding(4, 0, 0, 0);
        b.BackColor = primary ? GameArt.Accent : GameArt.Card;
        b.ForeColor = primary ? GameArt.OnAccent : GameArt.Text;
        b.Font = new Font("Segoe UI", 9f, primary ? FontStyle.Bold : FontStyle.Regular);
        b.FlatAppearance.BorderColor = primary ? GameArt.Accent : GameArt.CardBorder;
    }

    /// <summary>입어 보는 중에는 마우스를 올린 칸 설명으로 안내를 덮지 않는다.</summary>
    private void ShowHint(string text)
    {
        if (_trial is null) _catsHint.Text = text;
    }

    private static FlowLayoutPanel TileRow() => new()
    {
        AutoSize = true, WrapContents = false, Margin = new Padding(0), BackColor = GameArt.Background,
    };

    private static ShopTile ColorTile(string caption, string? hex) => new()
    {
        Caption = caption,
        Size = new Size(64, 84), // 10칸 + 사이 9칸 = 676px, 창 안쪽 폭에 딱 맞는다
        Margin = new Padding(0, 0, 4, 0),
        Art = (g, box) => DrawCatSwatch(g, box, hex),
    };

    /// <summary>칸 그림: 둥근 받침 위에 그 색 고양이. 연한 색은 받침을 어둡게 깔아야 보인다.</summary>
    private static void DrawCatSwatch(Graphics g, RectangleF box, string? hex)
    {
        float d = Math.Min(box.Width, box.Height);
        var disc = new RectangleF(box.Left + (box.Width - d) / 2, box.Top + (box.Height - d) / 2, d, d);
        if (hex is null || !ColorUtil.TryParse(hex, out var color))
        {
            // 아직 색이 없는 나만의 색 칸: 점선 원 + "+"
            using var pen = new Pen(GameArt.SubText, 1.2f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            var inner = RectangleF.Inflate(disc, -d * 0.12f, -d * 0.12f);
            g.DrawEllipse(pen, inner);
            using var plus = new Pen(GameArt.SubText, 1.6f);
            float cx = inner.Left + inner.Width / 2, cy = inner.Top + inner.Height / 2, arm = inner.Width * 0.2f;
            g.DrawLine(plus, cx - arm, cy, cx + arm, cy);
            g.DrawLine(plus, cx, cy - arm, cx, cy + arm);
            return;
        }
        using (var b = new SolidBrush(GameArt.SwatchBackFor(color))) g.FillEllipse(b, disc);
        var sprite = SpriteRegistry.Get("cat");
        var size = sprite.Measure(Math.Max(4, (int)(d * 0.5f)));
        var catBox = new RectangleF(disc.Left + (d - size.Width) / 2, disc.Top + (d - size.Height) / 2 + d * 0.04f, size.Width, size.Height);
        sprite.Draw(g, catBox, new RunnerPose(1, 0.0, 0.3), color);
    }

    private void BuildRunnerCards()
    {
        foreach (var c in _runnerCards) c.Dispose();
        _runnerCards.Clear();
        _cards.Controls.Clear();
        foreach (var r in _working.Runners)
        {
            var metric = r.Metric;
            var card = new RunnerCard(r.Id, () => _sampler.Read(metric));
            card.Picked += (_, _) => SelectRunner(card.RunnerId);
            card.VisibleToggled += (_, on) => SetEnabled(card.RunnerId, on);
            card.RunnerSizeChanged += (_, size) => EditRunner(card.RunnerId, x => x.Size = size);
            _runnerCards.Add(card);
            _cards.Controls.Add(card);
        }
        if (_runnerCards.Count > 0) _runnerCards[^1].Margin = new Padding(0);
    }

    private void SelectRunner(string id)
    {
        if (_selectedId == id) return;
        _trial = null;
        _selectedId = id;
        RefreshCards();
        RefreshShop();
        UpdateTrialBar();
        _closetHint.Text = DefaultClosetHint();
    }

    private void RefreshCards()
    {
        foreach (var card in _runnerCards)
        {
            var r = _working.Runners.FirstOrDefault(x => x.Id == card.RunnerId);
            if (r is null) continue;
            bool selected = r.Id == Selected?.Id;
            card.Bind(r, ColorName(r.Color), selected ? _trial?.Hex : null);
            card.Highlighted = selected;
        }
    }

    /// <summary>색 칸 20개의 잠김·입는 중 표시를 지금 생선 수와 선택한 고양이에 맞춘다.</summary>
    private void RefreshShop()
    {
        string? wearing = Selected?.Color;
        long fish = _rewards.Fish;
        foreach (var (color, tile) in _presetTiles)
        {
            tile.State = _rewards.Owns(color)
                ? (SameColor(color.Hex, wearing) ? TileState.Equipped : TileState.Owned)
                : (fish >= color.Price ? TileState.Buyable : TileState.TooExpensive);
            tile.Previewing = _trial?.Preset?.Id == color.Id;
        }
        int opened = _rewards.CustomSlotsOpened;
        for (int i = 0; i < _customTiles.Count; i++)
        {
            var tile = _customTiles[i];
            bool trying = _trial is { Preset: null } t && t.CustomIndex == i;
            // 입어 보는 칸은 고른 색을 미리 담아 보여준다.
            string? hex = i < opened ? _rewards.Current.CustomColors[i] : trying ? _trial!.Hex : null;
            tile.Art = (g, box) => DrawCatSwatch(g, box, hex);
            tile.Previewing = trying;
            tile.State = i < opened
                ? (SameColor(hex, wearing) ? TileState.Equipped : TileState.Owned)
                : i > opened ? TileState.Unavailable
                : fish >= tile.Price ? TileState.Buyable : TileState.TooExpensive;
            tile.Invalidate();
        }
    }

    private static bool SameColor(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private string ColorName(string hex)
    {
        if (RewardCatalog.FindPreset(hex) is { } p) return p.Name;
        int i = _rewards.Current.CustomColors.FindIndex(c => SameColor(c, hex));
        return i >= 0 ? $"나만의 색 {i + 1}" : hex.ToUpperInvariant();
    }

    private string DefaultCatsHint() =>
        Selected is { } r ? $"{RunnerName(r)}에게 입힐 색을 골라요. 위 카드를 누르면 다른 고양이를 고를 수 있어요." : "";

    private string PresetHint(PresetColor c)
    {
        if (_rewards.Owns(c))
            return SameColor(c.Hex, Selected?.Color) ? $"{c.Name} — 지금 입고 있어요" : $"{c.Name} — 눌러서 입혀요";
        return LockedHint(c.Name, c.Price, "눌러서 입어 보고 살 수 있어요");
    }

    private string CustomHint(int index)
    {
        string name = $"나만의 색 {index + 1}";
        int opened = _rewards.CustomSlotsOpened;
        if (index < opened)
        {
            bool wearing = SameColor(_rewards.Current.CustomColors[index], Selected?.Color);
            if (!_rewards.CanRecolorCustomSlots)
                return wearing ? $"{name} — 입고 있어요" : $"{name} — 눌러서 입혀요";
            return wearing
                ? $"{name} — 입고 있어요. 한 번 더 누르면 색을 바꿀 수 있어요"
                : $"{name} — 눌러서 입혀요. 오른쪽 클릭하면 색을 바꿔요";
        }
        if (index > opened) return $"{name} — 앞 칸을 먼저 열어야 해요 (생선 {GameArt.FormatCount(RewardCatalog.CustomSlotPrice(index))}개)";
        return LockedHint(name, RewardCatalog.CustomSlotPrice(index), "눌러서 색을 고르면 입어 보고 살 수 있어요");
    }

    private string LockedHint(string name, int price, string action)
    {
        long fish = _rewards.Fish;
        string cost = $"{name} — 생선 {GameArt.FormatCount(price)}개";
        return fish >= price ? $"{cost}. {action}" : $"{cost}. {GameArt.FormatCount(price - fish)}개 더 모으면 열 수 있어요 (입어 보기는 지금도 돼요)";
    }

    private void OnPresetClicked(PresetColor color, ShopTile tile)
    {
        if (Selected is not { } r) return;
        if (_rewards.Owns(color))
        {
            _trial = null; // 다른 칸을 누르면 입어 보던 건 내려놓는다
            Wear(color.Hex);
            UpdateTrialBar();
            _catsHint.Text = $"{RunnerName(r)}가 {color.Name} 색을 입었어요";
            return;
        }
        StartTrial(new Trial(color.Hex, $"{color.Name} 색", color.Price, color, -1));
    }

    private void OnCustomClicked(int index, ShopTile tile, MouseButtons button)
    {
        if (Selected is not { } r) return;
        int opened = _rewards.CustomSlotsOpened;
        if (index < opened)
        {
            EndTrial();
            string hex = _rewards.Current.CustomColors[index];
            // 입고 있는 칸을 다시 누르거나 오른쪽 클릭하면 색을 바꾼다. 10칸을 다 연 뒤에만 된다.
            if (button == MouseButtons.Right || SameColor(hex, r.Color))
            {
                if (!_rewards.CanRecolorCustomSlots)
                {
                    _catsHint.Text = $"나만의 색 10칸을 모두 열면 칸의 색을 바꿀 수 있어요 (지금 {opened}/{RewardCatalog.CustomSlotCount}칸)";
                    return;
                }
                if (PickColor(hex) is { } picked) Recolor(index, hex, picked);
                return;
            }
            Wear(hex);
            _catsHint.Text = $"{RunnerName(r)}가 나만의 색 {index + 1}을 입었어요";
            return;
        }
        if (button != MouseButtons.Left) return;
        if (index > opened)
        {
            EndTrial();
            _catsHint.Text = CustomHint(index);
            return;
        }
        // 색을 고르면 바로 사지 않고 미리보기 고양이에 입혀 본다. 고르다 취소하면 아무것도 바뀌지 않는다.
        string initial = _trial is { Preset: null } t && t.CustomIndex == index ? t.Hex : r.Color;
        if (PickColor(initial) is not { } chosen) return;
        StartTrial(new Trial(chosen, "고른 색", RewardCatalog.CustomSlotPrice(index), null, index));
    }

    // ───────── 입어 보기: 사기 전에 미리보기 고양이에만 색을 입혀 본다 ─────────

    private void StartTrial(Trial trial)
    {
        _trial = trial;
        RefreshCards();
        RefreshShop();
        UpdateTrialBar();
    }

    private void EndTrial()
    {
        if (_trial is null) return;
        _trial = null;
        RefreshCards();
        RefreshShop();
        UpdateTrialBar();
    }

    /// <summary>안내 상자와 사기 버튼을 입어 보는 상태와 지금 생선 수에 맞춘다.</summary>
    private void UpdateTrialBar()
    {
        _trialButtons.Visible = _trial is not null;
        if (_trial is not { } t || Selected is not { } r)
        {
            _catsHint.Text = DefaultCatsHint();
            return;
        }
        long fish = _rewards.Fish;
        bool affordable = fish >= t.Price;
        _trialBuy.Enabled = affordable;
        _trialBuy.BackColor = affordable ? GameArt.Accent : GameArt.Locked;
        _trialBuy.FlatAppearance.BorderColor = affordable ? GameArt.Accent : GameArt.CardBorder;
        _trialBuy.Text = affordable
            ? $"생선 {GameArt.FormatCount(t.Price)}개로 사기"
            : $"생선 {GameArt.FormatCount(t.Price - fish)}개 부족";
        _trialRepick.Visible = t.Preset is null;
        string what = t.Preset is null ? $"{t.CustomIndex + 1}번 칸에 담을 {t.Label}" : t.Label;
        _catsHint.Text = $"{RunnerName(r)}가 {what}을 입어 보는 중이에요";
    }

    private void RepickTrial()
    {
        if (_trial is not { Preset: null } t) return;
        if (PickColor(t.Hex) is { } picked) StartTrial(t with { Hex = picked });
    }

    private void BuyTrial()
    {
        if (_trial is not { } t || Selected is not { } r) return;
        bool bought = t.Preset is { } preset ? _rewards.TryBuy(preset) : _rewards.TryBuyCustomSlot(t.Hex);
        if (!bought)
        {
            UpdateTrialBar();
            return;
        }
        _trial = null;
        Wear(t.Hex);
        UpdateTrialBar();
        _catsHint.Text = t.Preset is { } p
            ? $"{p.Name} 색을 얻었어요! {RunnerName(r)}가 입었어요"
            : $"나만의 색 {t.CustomIndex + 1}번 칸을 열었어요! {RunnerName(r)}가 입었어요";
    }

    private bool ConfirmPurchase(string question, int price) =>
        MessageBox.Show(this, $"{question}\n\n남는 생선: {GameArt.FormatCount(_rewards.Fish - price)}개", "잠금 해제",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    private string? PickColor(string initial)
    {
        using var dlg = new ColorDialog { FullOpen = true, Color = ColorUtil.Parse(initial, Color.Black) };
        return dlg.ShowDialog(this) == DialogResult.OK ? ColorUtil.ToHex(dlg.Color) : null;
    }

    /// <summary>
    /// 나만의 색 칸의 색을 바꾼다. 그 색을 입고 있던 고양이도 새 색으로 갈아입힌다.
    /// 그대로 두면 칸에서 빠진 색을 계속 입고 있게 된다.
    /// </summary>
    private void Recolor(int index, string oldHex, string newHex)
    {
        _rewards.SetCustomColor(index, newHex);
        if (_rewards.OwnsColor(oldHex)) return; // 다른 칸이나 팔레트에 같은 색이 남아 있으면 그대로 입어도 된다
        bool changed = false;
        foreach (var r in _working.Runners.Where(r => SameColor(r.Color, oldHex)))
        {
            r.Color = newHex;
            changed = true;
        }
        if (changed) CommitAndRefresh();
    }

    private void Wear(string hex) => EditRunner(_selectedId, r => r.Color = hex);

    // ───────────────────────── 옷장 탭 ─────────────────────────

    private Control BuildClosetPage()
    {
        var page = new TableLayoutPanel { ColumnCount = 1, BackColor = GameArt.Background, Padding = new Padding(0, 6, 0, 0) };
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        page.Controls.Add(SectionHeader("러너 모음", "생선을 모아 새 친구를 데려오면 고양이 대신 달리게 할 수 있어요"));
        _runnerTiles.AutoSize = true;
        _runnerTiles.WrapContents = true;
        _runnerTiles.MaximumSize = new Size(680, 0);
        _runnerTiles.Margin = new Padding(0, 4, 0, 0);
        _runnerTiles.BackColor = GameArt.Background;
        page.Controls.Add(_runnerTiles);
        page.Controls.Add(HintBox(_closetHint));
        page.Controls.Add(new Panel { Dock = DockStyle.Fill });
        return page;
    }

    private string DefaultClosetHint() =>
        Selected is { } r ? $"고양이 탭에서 고른 {RunnerName(r)}에게 입혀요." : "";

    private void RefreshCloset()
    {
        foreach (Control c in _runnerTiles.Controls.Cast<Control>().ToList()) c.Dispose();
        _runnerTiles.Controls.Clear();
        long fish = _rewards.Fish;
        var wearing = Selected;

        foreach (var sprite in SpriteRegistry.All)
        {
            var s = sprite;
            int price = RewardCatalog.RunnerPrice(s.Id);
            bool owned = _rewards.OwnsRunner(s.Id);
            var tile = RunnerTile(s.DisplayName, (g, box) => DrawRunnerArt(g, box, s, wearing?.Color));
            tile.Price = price;
            tile.State = owned
                ? (string.Equals(wearing?.SpriteId, s.Id, StringComparison.OrdinalIgnoreCase) ? TileState.Equipped : TileState.Owned)
                : fish >= price ? TileState.Buyable : TileState.TooExpensive;
            tile.Click += (_, _) => OnRunnerClicked(s, price);
            tile.MouseEnter += (_, _) => _closetHint.Text = RunnerHint(s, price);
            tile.MouseLeave += (_, _) => _closetHint.Text = DefaultClosetHint();
            _runnerTiles.Controls.Add(tile);
        }

        // 다음에 나올 러너 자리. 그림이 생기기 전에도 목표가 보이게 한다.
        int next = RewardCatalog.NextUnreleasedRunnerPrice;
        var soon = RunnerTile("새 친구", DrawMystery);
        soon.Price = next;
        soon.State = TileState.Unavailable;
        soon.Cursor = Cursors.Default;
        soon.MouseEnter += (_, _) =>
            _closetHint.Text = $"새 러너를 준비하고 있어요. 생선 {GameArt.FormatCount(next)}개를 모아 두면 나오자마자 데려올 수 있어요";
        soon.MouseLeave += (_, _) => _closetHint.Text = DefaultClosetHint();
        _runnerTiles.Controls.Add(soon);
        _closetHint.Text = DefaultClosetHint();
    }

    private static ShopTile RunnerTile(string caption, Action<Graphics, RectangleF> art) => new()
    {
        Caption = caption, Size = new Size(128, 140), Margin = new Padding(0, 0, 8, 8), Art = art,
    };

    private static void DrawRunnerArt(Graphics g, RectangleF box, IRunnerSprite sprite, string? hex)
    {
        var color = ColorUtil.Parse(hex, Color.Black);
        GameArt.FillRound(g, box, 8, GameArt.SwatchBackFor(color));
        var size = sprite.Measure(Math.Max(4, (int)(box.Height * 0.55f)));
        var catBox = new RectangleF(box.Left + (box.Width - size.Width) / 2, box.Top + (box.Height - size.Height) / 2, size.Width, size.Height);
        sprite.Draw(g, catBox, new RunnerPose(1, 0.4, 0.5), color);
    }

    private static void DrawMystery(Graphics g, RectangleF box)
    {
        GameArt.FillRound(g, box, 8, GameArt.Mystery);
        using var font = new Font("Segoe UI", 26f, FontStyle.Bold);
        TextRenderer.DrawText(g, "?", font, Rectangle.Round(box), GameArt.SubText,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private string RunnerHint(IRunnerSprite sprite, int price)
    {
        if (_rewards.OwnsRunner(sprite.Id))
        {
            return string.Equals(Selected?.SpriteId, sprite.Id, StringComparison.OrdinalIgnoreCase)
                ? $"{sprite.DisplayName} — {(Selected is { } r ? RunnerName(r) : "")}로 달리고 있어요"
                : $"{sprite.DisplayName} — 눌러서 {(Selected is { } r2 ? SettingsForm.MetricName(r2.Metric) : "")} 러너로 바꿔요";
        }
        return LockedHint(sprite.DisplayName, price, "눌러서 데려와요");
    }

    private void OnRunnerClicked(IRunnerSprite sprite, int price)
    {
        if (Selected is not { } r) return;
        if (!_rewards.OwnsRunner(sprite.Id))
        {
            if (_rewards.Fish < price)
            {
                _closetHint.Text = RunnerHint(sprite, price);
                return;
            }
            if (!ConfirmPurchase($"{sprite.DisplayName}를 생선 {GameArt.FormatCount(price)}개로 데려올까요?", price)) return;
            if (!_rewards.TryBuyRunner(sprite.Id)) return;
        }
        EditRunner(r.Id, x => x.SpriteId = sprite.Id);
        RefreshCloset();
    }

    // ───────────────────────── 기록 탭 ─────────────────────────

    private Control BuildProfilePage()
    {
        var layout = new TableLayoutPanel { ColumnCount = 1, Padding = new Padding(8, 14, 8, 4), BackColor = GameArt.Background };
        for (int i = 0; i < 6; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _todayHeadline.AutoSize = true;
        _todayHeadline.Font = new Font("Segoe UI", 20f, FontStyle.Bold);
        _todayHeadline.Margin = new Padding(0, 0, 0, 2);
        layout.Controls.Add(_todayHeadline);

        _todayCompare.AutoSize = true;
        _todayCompare.Font = new Font("Segoe UI", 10f);
        _todayCompare.ForeColor = GameArt.SubText;
        _todayCompare.Margin = new Padding(2, 0, 0, 14);
        layout.Controls.Add(_todayCompare);

        var card = new CardPanel { Dock = DockStyle.Fill, Height = 150, Margin = new Padding(0) };
        _runnerStats.Dock = DockStyle.Fill;
        _runnerStats.ColumnCount = 4;
        _runnerStats.BackColor = GameArt.Card;
        _runnerStats.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
        _runnerStats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _runnerStats.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        _runnerStats.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        card.Controls.Add(_runnerStats);
        layout.Controls.Add(card);

        _totalLine.AutoSize = true;
        _totalLine.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
        _totalLine.Margin = new Padding(2, 18, 0, 2);
        layout.Controls.Add(_totalLine);

        _totalCompare.AutoSize = true;
        _totalCompare.Font = new Font("Segoe UI", 10f);
        _totalCompare.ForeColor = GameArt.SubText;
        _totalCompare.Margin = new Padding(2, 0, 0, 0);
        layout.Controls.Add(_totalCompare);

        _fishLine.AutoSize = true;
        _fishLine.Font = new Font("Segoe UI", 10f);
        _fishLine.ForeColor = GameArt.FishDark;
        _fishLine.Margin = new Padding(2, 14, 0, 0);
        layout.Controls.Add(_fishLine);

        layout.Controls.Add(new Panel { Dock = DockStyle.Fill });

        _pitchNote.AutoSize = true;
        _pitchNote.Font = new Font("Segoe UI", 8.5f);
        _pitchNote.ForeColor = GameArt.SubText;
        _pitchNote.Margin = new Padding(2, 8, 0, 0);
        layout.Controls.Add(_pitchNote);
        return layout;
    }

    /// <summary>러너 구성(색·표시 여부)이 바뀌었을 수 있으니 탭을 열 때마다 표를 새로 만든다. 숫자는 RefreshProfile이 채운다.</summary>
    private void BuildRunnerStatRows()
    {
        _runnerStats.SuspendLayout();
        foreach (Control c in _runnerStats.Controls.Cast<Control>().ToList()) c.Dispose();
        _runnerStats.Controls.Clear();
        _runnerStats.RowStyles.Clear();
        _runnerStatLabels.Clear();

        // 줄 높이를 고정하고 남는 높이는 맨 아래 빈 줄에 몰아준다.
        // 그러지 않으면 카드의 남는 높이가 마지막 고양이 줄에 붙어서, 글자는 가운데로 내려가는데 아이콘만 위에 남는다.
        _runnerStats.RowCount = _working.Runners.Count + 2;
        _runnerStats.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        for (int i = 0; i < _working.Runners.Count; i++) _runnerStats.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        _runnerStats.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _runnerStats.Controls.Add(StatHeader("오늘"), 2, 0);
        _runnerStats.Controls.Add(StatHeader("지금까지"), 3, 0);

        int row = 1;
        foreach (var r in _working.Runners)
        {
            _runnerStats.Controls.Add(new RunnerIcon(r) { Size = new Size(48, 34), Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 4, 0) }, 0, row);
            _runnerStats.Controls.Add(StatCell(RunnerName(r) + (r.Enabled ? "" : " (숨김)"), ContentAlignment.MiddleLeft), 1, row);
            var today = StatCell("", ContentAlignment.MiddleRight);
            var total = StatCell("", ContentAlignment.MiddleRight);
            _runnerStats.Controls.Add(today, 2, row);
            _runnerStats.Controls.Add(total, 3, row);
            _runnerStatLabels[r.Id] = (today, total);
            row++;
        }
        _runnerStats.ResumeLayout();
    }

    private static Label StatHeader(string text) => new()
    {
        Text = text, TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill, AutoSize = false, Height = 22,
        ForeColor = GameArt.SubText, Margin = new Padding(0),
    };

    private static Label StatCell(string text, ContentAlignment align) => new()
    {
        Text = text, TextAlign = align, Dock = DockStyle.Fill, AutoSize = false, Height = 38,
        Font = new Font("Segoe UI", 10.5f), Margin = new Padding(0),
    };

    private void RefreshProfile()
    {
        double today = _stats.TodayMillimeters();
        _todayHeadline.Text = today > 0 ? $"오늘 {DistanceText.Format(today)} 달렸어요" : "오늘은 아직 쉬는 중이에요";
        _todayCompare.Text = today > 0 ? DistanceText.Compare(today) : "고양이가 달리기 시작하면 여기 쌓여요";

        foreach (var (id, labels) in _runnerStatLabels)
        {
            labels.Today.Text = DistanceText.Format(_stats.TodayMillimeters(id));
            labels.Total.Text = DistanceText.Format(_stats.Total(id).Millimeters);
        }

        double total = _stats.TotalMillimeters();
        _totalLine.Text = $"지금까지 총 {DistanceText.Format(total)}";
        _totalCompare.Text = DistanceText.Compare(total);
        _fishLine.Text = $"지금까지 생선 {GameArt.FormatCount(_rewards.EarnedFish)}개를 모았고, " +
                         $"{GameArt.FormatCount(_rewards.Current.SpentFish)}개를 썼어요";

        var pitch = MonitorPitch.For(MonitorCatalog.Resolve(_working.MonitorDeviceName));
        _pitchNote.Text = pitch.Measured
            ? $"{pitch.MonitorName ?? "모니터"} 실제 크기({pitch.WidthMm} × {pitch.HeightMm} mm)로 계산했어요 · 1px = {pitch.MillimetersPerPixel:0.000} mm"
            : $"모니터 크기를 알 수 없어 화면 배율로 추정했어요 · 1px ≈ {pitch.MillimetersPerPixel:0.000} mm";
    }

    // ───────────────────────── 일반 탭 ─────────────────────────

    private Control BuildGeneralPage()
    {
        var page = new TableLayoutPanel { ColumnCount = 1, BackColor = GameArt.Background, Padding = new Padding(0, 6, 0, 0) };
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var card = new CardPanel { Dock = DockStyle.Top, Height = 96, Margin = new Padding(0, 6, 0, 0), Padding = new Padding(16, 14, 16, 12) };
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = GameArt.Card };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.Controls.Add(grid);

        grid.Controls.Add(new Label { Text = "달릴 모니터", AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold), Margin = new Padding(0, 8, 16, 4) }, 0, 0);
        var monitorRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 6), BackColor = GameArt.Card };
        _monitor.DropDownStyle = ComboBoxStyle.DropDownList;
        _monitor.Width = 380;
        _monitor.DisplayMember = nameof(MonitorCatalog.MonitorItem.Label);
        _monitor.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _monitor.SelectedItem is not MonitorCatalog.MonitorItem m) return;
            _working.MonitorDeviceName = m.DeviceName;
            Commit();
        };
        var refresh = new Button { Text = "다시 찾기", AutoSize = true, Margin = new Padding(6, 0, 0, 0), BackColor = GameArt.Card };
        refresh.Click += (_, _) => LoadMonitors();
        monitorRow.Controls.Add(_monitor);
        monitorRow.Controls.Add(refresh);
        grid.Controls.Add(monitorRow, 1, 0);

        _hideFullScreen.Text = "전체 화면 앱(게임·발표) 실행 중에는 숨기기";
        _hideFullScreen.AutoSize = true;
        _hideFullScreen.Margin = new Padding(0, 6, 0, 0);
        _hideFullScreen.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _working.HideInFullScreen = _hideFullScreen.Checked;
            Commit();
        };
        grid.Controls.Add(_hideFullScreen, 1, 1);

        page.Controls.Add(card);
        return page;
    }

    // ───────────────────────── 데이터 ↔ 화면 ─────────────────────────

    private void LoadAll()
    {
        _loading = true;
        try
        {
            if (_working.Runners.All(r => r.Id != _selectedId)) _selectedId = _working.Runners.FirstOrDefault()?.Id ?? "cpu";
            BuildRunnerCards();
            _hideFullScreen.Checked = _working.HideInFullScreen;
            LoadMonitors();
        }
        finally
        {
            _loading = false;
        }
        RefreshAllViews();
    }

    private void RefreshAllViews()
    {
        RefreshCards();
        RefreshShop();
        UpdateTrialBar();
        // 기록을 보는 중에 트레이 메뉴로 표시 여부를 바꾸면 표의 "(숨김)"도 따라가게 한다.
        if (_page == Page.Profile) BuildRunnerStatRows();
        if (_page == Page.Closet) RefreshCloset();
    }

    private void LoadMonitors()
    {
        bool wasLoading = _loading;
        _loading = true;
        try
        {
            var items = MonitorCatalog.List();
            _monitor.Items.Clear();
            foreach (var m in items) _monitor.Items.Add(m);
            // 저장된 모니터가 지금 없으면(분리됨) 실제로 쓰이는 주 모니터를 선택해 보여준다.
            string current = MonitorCatalog.Resolve(_working.MonitorDeviceName).DeviceName;
            _monitor.SelectedItem = items.FirstOrDefault(m => m.DeviceName == current) ?? items.FirstOrDefault();
        }
        finally
        {
            _loading = wasLoading;
        }
    }

    private void RefreshWallet()
    {
        long fish = _rewards.Fish;
        _header.SetWallet(fish, _rewards.NextFishProgress);
        if (fish == _lastFish) return;
        _lastFish = fish;
        // 생선이 늘면 잠긴 칸이 "살 수 있음"으로, 입어 보던 색의 사기 버튼이 켜짐으로 바뀔 수 있다.
        RefreshShop();
        if (_trial is not null) UpdateTrialBar();
        if (_page == Page.Closet) RefreshCloset();
    }

    private void SetEnabled(string id, bool enabled) => EditRunner(id, r => r.Enabled = enabled);

    private void EditRunner(string id, Action<RunnerConfig> change)
    {
        if (_loading) return;
        var r = _working.Runners.FirstOrDefault(x => x.Id == id);
        if (r is null) return;
        change(r);
        CommitAndRefresh();
    }

    private void CommitAndRefresh()
    {
        Commit();
        RefreshCards();
        RefreshShop();
    }

    private void Commit() => _store.Update(_working.Clone());

    private void OnStoreChanged(object? sender, EventArgs e)
    {
        // 내가 방금 저장한 값이면 화면과 같으므로 표시 여부만 비교해서 다를 때만 다시 읽는다.
        var current = _store.Current;
        bool differs = current.Runners.Count != _working.Runners.Count ||
                       current.Runners.Zip(_working.Runners).Any(p => p.First.Enabled != p.Second.Enabled);
        if (!differs) return;
        _working = current.Clone();
        LoadAll();
    }

    private void OnRewardsChanged(object? sender, EventArgs e)
    {
        _lastFish = -1; // 생선을 썼으니 지갑과 칸 상태를 바로 다시 그린다
        RefreshWallet();
        RefreshCards();
    }

    private void ResetSelected()
    {
        if (Selected is not { } r) return;
        EndTrial();
        var def = AppSettings.CreateDefault().Runners.FirstOrDefault(d => d.Id == r.Id);
        if (def is null) return;
        EditRunner(r.Id, x =>
        {
            x.Color = def.Color;
            x.Size = def.Size;
        });
    }

    public static string RunnerName(RunnerConfig r) => $"{MetricName(r.Metric)} {SpriteRegistry.Get(r.SpriteId).DisplayName}";

    public static string MetricName(MetricKind m) => m switch
    {
        MetricKind.Cpu => "CPU",
        MetricKind.Memory => "메모리",
        _ => m.ToString(),
    };
}
