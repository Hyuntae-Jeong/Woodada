using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Woodada.Core;
using Woodada.Platform;
using Woodada.Rewards;
using Woodada.Settings;
using Woodada.Sprites;
using Woodada.Stats;

namespace Woodada.UI;

/// <summary>
/// 맨 위: 가진 생선 · 다음 생선까지 진행 막대.
/// 고양이 — CPU·메모리 고양이 카드(표시 · 미리보기 · 크기)와 색 칸 20개(기본 10 + 나만의 색 10).
/// 옷장 — 러너 모음. 기록 — 달린 거리. 일반 — 모니터 · 전체 화면.
/// 냥뽑기 — 개발용 빌드는 스페셜 스킨 뽑기, 일반 버전은 자물쇠를 단 예고 화면(COMING SOON).
/// 개발용 빌드에서만: 고양이 탭 맨 아래 스페셜 스킨 줄.
/// 바꾸는 즉시 오버레이에 반영하고 저장한다.
/// </summary>
internal sealed class SettingsWindow : Window
{
    public enum Page { Cats, Closet, Profile, General, Gacha }

    private readonly IPlatform _platform = PlatformServices.Current;
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
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    // 옷장의 출시 예정 칸에 마우스를 올렸을 때 제자리 달리기를 보여 주는 타이머.
    private readonly DispatcherTimer _teaserTimer = new() { Interval = TimeSpan.FromMilliseconds(110) };
    private Action? _teaserStep;

    // 고양이 탭
    private readonly StackPanel _cards = new() { Orientation = Orientation.Horizontal };
    private readonly List<RunnerCard> _runnerCards = new();
    private readonly List<(PresetColor Color, ShopTile Tile)> _presetTiles = new();
    private readonly List<ShopTile> _customTiles = new();
    private readonly TextBlock _catsHint = new();
    private readonly StackPanel _trialButtons = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly GameButton _trialBuy = new("", primary: true);
    private readonly GameButton _trialRepick = new("다른 색");
    private readonly GameButton _trialCancel = new("그만");
    private Trial? _trial;

    /// <summary>사기 전에 입어 보는 색. Preset이 없으면 나만의 색 칸(CustomIndex)이다.</summary>
    private sealed record Trial(string Hex, string Label, int Price, PresetColor? Preset, int CustomIndex);

    // 고양이 탭: 스페셜 스킨 줄 (개발용 빌드에서만)
    private readonly List<(RunnerSkin Skin, ShopTile Tile)> _skinTiles = new();
    // 스킨 칸 고양이가 제자리 달리기를 하도록 다시 그리는 타이머. 고양이 탭을 볼 때만 일한다.
    private readonly DispatcherTimer _skinTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly GameButton _gotoGacha = new("냥뽑기로 가기") { IsVisible = false, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };

    // 뽑기 탭 (개발용 빌드에서만)
    private readonly GachaStage _stage = new() { Height = 210 };
    private readonly Grid _pullRow = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
    private readonly TextBlock _pullTitle = new();
    private readonly TextBlock _pullSub = new();
    private readonly PriceLabel _gachaPrice = new() { Price = RewardCatalog.GachaPrice };
    private readonly GameButton _pull = new("한 번 뽑기", primary: true) { MinWidth = 96 };
    private readonly Grid _resultRow = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), IsVisible = false };
    private readonly TextBlock _resultName = new();
    private readonly TextBlock _resultStars = new();
    private readonly TextBlock _resultDesc = new();
    private readonly StackPanel _wearButtons = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    private readonly GameButton _again = new("한 번 더", primary: true);
    private readonly TextBlock _dexCount = new();
    private readonly List<(SkinInfo Info, SkinRunnerView View, TextBlock Name, TextBlock Tier)> _dexSlots = new();
    private readonly ComboBox _devPick = new() { MinWidth = 110 };
    private List<RunnerSkin?> _devPickSkins = new();
    private readonly CheckBox _devFree = new() { Content = "생선 안 쓰고 뽑기" };
    /// <summary>방금 뽑아 연출 중이거나 결과를 보여 주는 스킨.</summary>
    private RunnerSkin _pulled;
    // 냥뽑기 예고 (일반 버전)
    private readonly TextBlock _teaserSub = new();

    // 옷장 탭
    private readonly WrapPanel _runnerTiles = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock _closetHint = new();

    // 기록 탭
    private readonly TextBlock _todayHeadline = new();
    private readonly TextBlock _todayCompare = new();
    private readonly Grid _runnerStats = new();
    private readonly Dictionary<string, (TextBlock Today, TextBlock Total)> _runnerStatLabels = new();
    private readonly TextBlock _totalLine = new();
    private readonly TextBlock _totalCompare = new();
    private readonly TextBlock _fishLine = new();
    private readonly TextBlock _pitchNote = new();

    // 일반 탭
    private readonly ComboBox _monitor = new();
    private List<ScreenCatalog.MonitorItem> _monitorItems = new();
    private readonly CheckBox _hideFullScreen = new();

    public SettingsWindow(SettingsStore store, StatsStore stats, RewardStore rewards, MetricSampler sampler)
    {
        _store = store;
        _stats = stats;
        _rewards = rewards;
        _sampler = sampler;
        _working = store.Current.Clone();
        _selectedId = _working.Runners.FirstOrDefault()?.Id ?? "cpu";

        Title = AppInfo.IsDev ? "Woodada (개발용)" : "Woodada";
        Icon = AppIcon.Window;
        Width = 700;
        // 개발용 빌드는 고양이 탭에 스킨 줄이 하나 더 있어서 그만큼 길다.
        Height = AppInfo.IsDev ? 804 : 690;
        CanResize = false;
        CanMinimize = false;
        CanMaximize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = GameArt.Brush(GameArt.Background);
        Foreground = GameArt.Brush(GameArt.Text);
        FontSize = GameArt.FontBody;

        BuildLayout();
        LoadAll();
        RefreshWallet();
        ShowPage(Page.Cats);

        // 표시 여부를 다른 곳에서 바꿔도 열려 있는 설정 창이 따라가게 한다.
        _store.Changed += OnStoreChanged;
        _rewards.Changed += OnRewardsChanged;
        // 창이 열려 있는 동안 고양이가 달리는 만큼 생선과 기록이 올라가게 한다.
        _tick.Tick += (_, _) =>
        {
            RefreshWallet();
            if (_page == Page.Profile) RefreshProfile();
        };
        _tick.Start();
        _teaserTimer.Tick += (_, _) => _teaserStep?.Invoke();
        _skinTimer.Tick += (_, _) => AnimateSkinTiles();
        if (AppInfo.IsDev) _skinTimer.Start();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
        Closed += (_, _) =>
        {
            _store.Changed -= OnStoreChanged;
            _rewards.Changed -= OnRewardsChanged;
            _tick.Stop();
            _teaserTimer.Stop();
            _skinTimer.Stop();
        };
    }

    public void ShowPage(Page page)
    {
        if (page != Page.Cats) EndTrial();
        _page = page;
        foreach (var (p, (pill, view)) in _pages)
        {
            pill.Selected = p == page;
            view.IsVisible = p == page;
        }
        if (page == Page.Profile)
        {
            BuildRunnerStatRows();
            RefreshProfile();
        }
        if (page == Page.Closet) RefreshCloset();
        if (page == Page.Gacha)
        {
            // 다시 들어오면 지난 결과 대신 뽑기 전 화면부터(다 모았으면 "도감 완성!"). 연출 중이면 그대로 둔다.
            if (!_stage.IsPlaying)
            {
                _stage.ShowIdle();
                _pullRow.IsVisible = true;
                _resultRow.IsVisible = false;
            }
            RefreshGacha();
        }
    }

    private RunnerConfig? Selected => _working.Runners.FirstOrDefault(r => r.Id == _selectedId) ?? _working.Runners.FirstOrDefault();

    // ───────────────────────── 레이아웃 ─────────────────────────

    private void BuildLayout()
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("72,Auto,*,Auto") };
        Content = root;

        root.Children.Add(_header);

        var nav = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 10, 12, 4) };
        Grid.SetRow(nav, 1);
        root.Children.Add(nav);

        _content.Margin = new Thickness(12, 4, 12, 0);
        Grid.SetRow(_content, 2);
        root.Children.Add(_content);

        AddPage(nav, Page.Cats, "고양이", BuildCatsPage());
        AddPage(nav, Page.Closet, "옷장", BuildClosetPage());
        AddPage(nav, Page.Profile, "기록", BuildProfilePage());
        AddPage(nav, Page.General, "일반", BuildGeneralPage());
        // 스페셜 스킨은 아직 출시 전이라 일반 버전은 자물쇠를 단 예고 화면만 보여 준다. 뽑기는 개발용 빌드에서만 된다.
        AddPage(nav, Page.Gacha, "냥뽑기", AppInfo.IsDev ? BuildGachaPage() : BuildGachaTeaserPage(), locked: !AppInfo.IsDev);

        var close = new GameButton("닫기") { MinWidth = 80, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 6, 12, 12) };
        close.Click += (_, _) => Close();
        Grid.SetRow(close, 3);
        root.Children.Add(close);
    }

    private void AddPage(StackPanel nav, Page page, string title, Control view, bool locked = false)
    {
        var pill = new NavPill(title) { Locked = locked };
        pill.Click += (_, _) => ShowPage(page);
        nav.Children.Add(pill);
        view.IsVisible = false;
        _content.Children.Add(view);
        _pages[page] = (pill, view);
    }

    private static TextBlock Label(string text, double size, Color color, FontWeight weight = FontWeight.Normal) => new()
    {
        Text = text, FontSize = size, Foreground = GameArt.Brush(color), FontWeight = weight,
    };

    private static Control SectionHeader(string title, string note)
    {
        var t = Label(title, GameArt.FontSection, GameArt.Text, FontWeight.Bold);
        t.Margin = new Thickness(0, 12, 8, 4);
        t.VerticalAlignment = VerticalAlignment.Bottom;
        var n = Label(note, GameArt.FontBody, GameArt.SubText);
        n.Margin = new Thickness(0, 0, 0, 5);
        n.VerticalAlignment = VerticalAlignment.Bottom;
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { t, n } };
    }

    private static Border HintBox(TextBlock label, Control? right = null)
    {
        label.FontSize = GameArt.FontBody;
        label.Foreground = GameArt.Brush(GameArt.Text);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        label.Margin = new Thickness(12, 0);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { label } };
        if (right is not null)
        {
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);
        }
        return new Border
        {
            Background = GameArt.Brush(GameArt.AccentSoft), Height = 40, Margin = new Thickness(0, 10, 0, 0),
            CornerRadius = new CornerRadius(6), Child = grid,
        };
    }

    // ───────────────────────── 고양이 탭 ─────────────────────────

    private Control BuildCatsPage()
    {
        var page = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        page.Children.Add(_cards);

        page.Children.Add(SectionHeader("기본 색", "까망·하양은 처음부터 있어요. 나머지는 생선으로 열어요"));
        var presets = TileRow();
        foreach (var color in RewardCatalog.Presets)
        {
            var tile = ColorTile(color.Name, color.Hex);
            tile.Price = color.Price;
            var c = color;
            tile.Click += (_, _) => OnPresetClicked(c);
            tile.PointerEntered += (_, _) => ShowHint(PresetHint(c));
            tile.PointerExited += (_, _) => ShowHint(DefaultCatsHint());
            presets.Children.Add(tile);
            _presetTiles.Add((color, tile));
        }
        page.Children.Add(presets);

        page.Children.Add(SectionHeader("나만의 색", "직접 고른 색을 담아요. 앞 칸부터 열리고, 10칸을 다 열면 색을 바꿀 수 있어요"));
        var customs = TileRow();
        for (int i = 0; i < RewardCatalog.CustomSlotCount; i++)
        {
            int index = i;
            var tile = ColorTile($"{i + 1}번 칸", null);
            tile.Price = RewardCatalog.CustomSlotPrice(i);
            tile.Click += (_, _) => OnCustomClicked(index, rightClick: false);
            tile.RightClick += (_, _) => OnCustomClicked(index, rightClick: true);
            tile.PointerEntered += (_, _) => ShowHint(CustomHint(index));
            tile.PointerExited += (_, _) => ShowHint(DefaultCatsHint());
            customs.Children.Add(tile);
            _customTiles.Add(tile);
        }
        page.Children.Add(customs);

        // 색 칸 아래 안내 상자. 입어 보는 중에는 오른쪽에 사기 · 다른 색 · 그만 버튼이 붙는다.
        _trialButtons.Margin = new Thickness(0, 0, 6, 0);
        _trialButtons.VerticalAlignment = VerticalAlignment.Center;
        _trialButtons.IsVisible = false;
        _trialBuy.Click += (_, _) => BuyTrial();
        _trialRepick.Click += (_, _) => RepickTrial();
        _trialCancel.Click += (_, _) => EndTrial();
        _trialButtons.Children.Add(_trialBuy);
        _trialButtons.Children.Add(_trialRepick);
        _trialButtons.Children.Add(_trialCancel);
        if (AppInfo.IsDev) page.Children.Add(BuildSkinRow());
        _gotoGacha.Click += (_, _) => ShowPage(Page.Gacha);
        var hintButtons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { _trialButtons, _gotoGacha } };
        page.Children.Add(HintBox(_catsHint, hintButtons));
        return page;
    }

    /// <summary>입어 보는 중에는 마우스를 올린 칸 설명으로 안내를 덮지 않는다.</summary>
    private void ShowHint(string text)
    {
        if (_trial is not null) return;
        _catsHint.Text = text;
        _gotoGacha.IsVisible = false;
    }

    // 10칸 + 사이 9칸 = 676, 창 안쪽 폭에 딱 맞는다.
    private static StackPanel TileRow() => new() { Orientation = Orientation.Horizontal, Spacing = 4 };

    private static ShopTile ColorTile(string caption, string? hex) => new()
    {
        Caption = caption,
        Width = 64,
        Height = 84,
        Art = (ctx, box, scaling) => DrawCatSwatch(ctx, box, hex, scaling),
    };

    /// <summary>칸 그림: 둥근 받침 위에 그 색 고양이. 연한 색은 받침을 어둡게 깔아야 보인다.</summary>
    private static void DrawCatSwatch(DrawingContext ctx, Rect box, string? hex, double scaling)
    {
        double d = Math.Min(box.Width, box.Height);
        var disc = new Rect(box.X + (box.Width - d) / 2, box.Y + (box.Height - d) / 2, d, d);
        if (hex is null || !ColorUtil.TryParse(hex, out var color))
        {
            // 아직 색이 없는 나만의 색 칸: 점선 원 + "+"
            var inner = disc.Deflate(d * 0.12);
            var sub = GameArt.Brush(GameArt.SubText);
            ctx.DrawEllipse(null, new Pen(sub, 1.2, new DashStyle(new double[] { 3, 2 }, 0)), inner);
            var plus = new Pen(sub, 1.6);
            var c = inner.Center;
            double arm = inner.Width * 0.2;
            ctx.DrawLine(plus, new Point(c.X - arm, c.Y), new Point(c.X + arm, c.Y));
            ctx.DrawLine(plus, new Point(c.X, c.Y - arm), new Point(c.X, c.Y + arm));
            return;
        }
        ctx.DrawEllipse(GameArt.Brush(GameArt.SwatchBackFor(color)), null, disc);
        var sprite = SpriteRegistry.Get("cat");
        var size = sprite.Measure(Math.Max(4, d * 0.5));
        var catBox = new Rect(disc.X + (d - size.Width) / 2, disc.Y + (d - size.Height) / 2 + d * 0.04, size.Width, size.Height);
        sprite.Draw(ctx, catBox, new RunnerPose(1, 0.0, 0.3), color, scaling);
    }

    private void BuildRunnerCards()
    {
        _runnerCards.Clear();
        _cards.Children.Clear();
        foreach (var r in _working.Runners)
        {
            var metric = r.Metric;
            var card = new RunnerCard(r.Id, () => _sampler.Read(metric));
            card.Picked += (_, _) => SelectRunner(card.RunnerId);
            card.VisibleToggled += (_, on) => SetEnabled(card.RunnerId, on);
            card.RunnerSizeChanged += (_, size) => EditRunner(card.RunnerId, x => x.Size = size);
            _runnerCards.Add(card);
            _cards.Children.Add(card);
        }
        if (_runnerCards.Count > 0) _runnerCards[^1].Margin = new Thickness(0);
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
            tile.Art = (ctx, box, scaling) => DrawCatSwatch(ctx, box, hex, scaling);
            tile.Previewing = trying;
            tile.State = i < opened
                ? (SameColor(hex, wearing) ? TileState.Equipped : TileState.Owned)
                : i > opened ? TileState.Unavailable
                : fish >= tile.Price ? TileState.Buyable : TileState.TooExpensive;
        }
        RefreshSkinTiles();
    }

    private static bool SameColor(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private string ColorName(string hex)
    {
        if (RewardCatalog.FindPreset(hex) is { } p) return p.Name;
        int i = _rewards.Current.CustomColors.FindIndex(c => SameColor(c, hex));
        return i >= 0 ? $"나만의 색 {i + 1}" : hex.ToUpperInvariant();
    }

    private string DefaultCatsHint() =>
        Selected is { } r
            ? $"{RunnerName(r)}에게 입힐 {(AppInfo.IsDev ? "색과 스킨을" : "색을")} 골라요. 위 카드를 누르면 다른 고양이를 고를 수 있어요."
            : "";

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

    private void OnPresetClicked(PresetColor color)
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

    private async void OnCustomClicked(int index, bool rightClick)
    {
        if (Selected is not { } r) return;
        int opened = _rewards.CustomSlotsOpened;
        if (index < opened)
        {
            EndTrial();
            string hex = _rewards.Current.CustomColors[index];
            // 입고 있는 칸을 다시 누르거나 오른쪽 클릭하면 색을 바꾼다. 10칸을 다 연 뒤에만 된다.
            if (rightClick || SameColor(hex, r.Color))
            {
                if (!_rewards.CanRecolorCustomSlots)
                {
                    _catsHint.Text = $"나만의 색 10칸을 모두 열면 칸의 색을 바꿀 수 있어요 (지금 {opened}/{RewardCatalog.CustomSlotCount}칸)";
                    return;
                }
                if (await PickColorAsync(hex) is { } picked) Recolor(index, hex, picked);
                return;
            }
            Wear(hex);
            _catsHint.Text = $"{RunnerName(r)}가 나만의 색 {index + 1}을 입었어요";
            return;
        }
        if (rightClick) return;
        if (index > opened)
        {
            EndTrial();
            _catsHint.Text = CustomHint(index);
            return;
        }
        // 색을 고르면 바로 사지 않고 미리보기 고양이에 입혀 본다. 고르다 취소하면 아무것도 바뀌지 않는다.
        string initial = _trial is { Preset: null } t && t.CustomIndex == index ? t.Hex : r.Color;
        if (await PickColorAsync(initial) is not { } chosen) return;
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
        _trialButtons.IsVisible = _trial is not null;
        _gotoGacha.IsVisible = false;
        if (_trial is not { } t || Selected is not { } r)
        {
            _catsHint.Text = DefaultCatsHint();
            return;
        }
        long fish = _rewards.Fish;
        bool affordable = fish >= t.Price;
        _trialBuy.IsEnabled = affordable;
        _trialBuy.Text = affordable
            ? $"생선 {GameArt.FormatCount(t.Price)}개로 사기"
            : $"생선 {GameArt.FormatCount(t.Price - fish)}개 부족";
        _trialRepick.IsVisible = t.Preset is null;
        string what = t.Preset is null ? $"{t.CustomIndex + 1}번 칸에 담을 {t.Label}" : t.Label;
        _catsHint.Text = $"{RunnerName(r)}가 {what}을 입어 보는 중이에요";
    }

    private async void RepickTrial()
    {
        if (_trial is not { Preset: null } t) return;
        if (await PickColorAsync(t.Hex) is { } picked) StartTrial(t with { Hex = picked });
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

    private Task<bool> ConfirmPurchaseAsync(string question, int price) =>
        Dialogs.ConfirmAsync(this, "잠금 해제", $"{question}\n\n남는 생선: {GameArt.FormatCount(_rewards.Fish - price)}개");

    private Task<string?> PickColorAsync(string initial) => Dialogs.PickColorAsync(this, initial);

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

    // ───────── 고양이 탭: 스페셜 스킨 줄 (개발용 빌드에서만) ─────────

    /// <summary>
    /// 기본 모습 + 스킨들. 가진 칸은 고른 고양이 색으로 제자리 달리기를 하고, 없는 칸은 실루엣과 물음표다.
    /// 스킨이 늘어도 한 줄에 들어오게 칸 너비를 나눠 갖는다.
    /// </summary>
    private Control BuildSkinRow()
    {
        var skins = RewardCatalog.Skins.Select(s => s.Skin).Prepend(RunnerSkin.None).ToList();
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",8,", skins.Select(_ => "*"))) };
        foreach (var (skin, column) in skins.Select((s, i) => (s, i * 2)))
        {
            var runner = new TreadmillRunner();
            var tile = new ShopTile { Height = 84 };
            Grid.SetColumn(tile, column);
            tile.Art = (ctx, box, scaling) => DrawSkinTileArt(ctx, box, skin, runner, scaling);
            tile.Click += (_, _) => OnSkinClicked(skin);
            // 나갈 때 안내를 되돌리지 않는다. 없는 칸의 "뽑기로 가기" 버튼을 누르러 마우스를 옮겨도 남아 있게.
            tile.PointerEntered += (_, _) => ShowSkinHint(skin);
            row.Children.Add(tile);
            _skinTiles.Add((skin, tile));
        }
        return new StackPanel
        {
            Children = { SectionHeader("스페셜 스킨", "뽑기로 얻어요. 색은 그대로 두고 겉모습만 바뀌어요"), row },
        };
    }

    private void DrawSkinTileArt(DrawingContext ctx, Rect box, RunnerSkin skin, TreadmillRunner runner, double scaling)
    {
        double floor = box.Bottom - 8, height = Math.Min(28, box.Height * 0.5);
        if (!_rewards.OwnsSkin(skin))
        {
            SkinRunnerView.DrawStage(ctx, box, 6, light: false, floorInset: 8);
            using (ctx.PushClip(box)) SkinRunnerView.DrawMystery(ctx, box.Center.X, floor, height, scaling);
            return;
        }
        var color = ColorUtil.Parse(Selected?.Color, Colors.Black);
        // 색 칸처럼 몸이 바탕에 묻히면 반대 톤 바탕을 깐다(까망 고양이는 밝게, 하양·유령은 어둡게).
        var look = RunnerSkins.LookColor(skin, color);
        bool light = !GameArt.LowContrast(look, UI.Theme.Current.SwatchBack);
        SkinRunnerView.DrawStage(ctx, box, 6, light, floorInset: 8);
        // 플래시 잔상·얼음 서리·줄줄이 새끼는 뒤로 길게 끌려서, 몸을 앞에 두어야 칸 안에 들어온다.
        double cx = box.Center.X + skin switch
        {
            RunnerSkin.Flash or RunnerSkin.Ice => height * 0.4,
            RunnerSkin.Kittens => height * 1.1,
            _ => 0,
        };
        using (ctx.PushClip(box)) runner.Draw(ctx, cx, floor, height, skin, color, 30, scaling);
    }

    private void AnimateSkinTiles()
    {
        if (_page != Page.Cats) return;
        foreach (var (skin, tile) in _skinTiles)
            if (_rewards.OwnsSkin(skin)) tile.Redraw();
    }

    private void RefreshSkinTiles()
    {
        var wearing = RunnerSkins.Parse(Selected?.Skin);
        foreach (var (skin, tile) in _skinTiles)
        {
            bool owned = _rewards.OwnsSkin(skin);
            tile.Caption = skin == RunnerSkin.None ? "기본 모습" : owned ? RewardCatalog.FindSkin(skin)!.Name : "???";
            tile.LockedNote = owned ? null : "뽑기에서 얻어요";
            tile.State = !owned ? TileState.TooExpensive : skin == wearing ? TileState.Equipped : TileState.Owned;
            tile.Redraw();
        }
    }

    private void ShowSkinHint(RunnerSkin skin)
    {
        if (_trial is not null || Selected is not { } r) return;
        bool wearing = RunnerSkins.Parse(r.Skin) == skin;
        _gotoGacha.IsVisible = false;
        if (skin == RunnerSkin.None)
        {
            _catsHint.Text = wearing ? "기본 모습 — 지금 스킨 없이 달리고 있어요" : "기본 모습 — 눌러서 스킨을 벗어요";
            return;
        }
        if (!_rewards.OwnsSkin(skin))
        {
            _catsHint.Text = $"아직 없는 스킨이에요. 뽑기에서 생선 {GameArt.FormatCount(RewardCatalog.GachaPrice)}개로 뽑을 수 있어요";
            _gotoGacha.IsVisible = true;
            return;
        }
        var info = RewardCatalog.FindSkin(skin)!;
        _catsHint.Text = wearing ? $"{info.Name} — 지금 입고 있어요" : $"{info.Name} — 눌러서 {RunnerName(r)}에게 입혀요";
    }

    private void OnSkinClicked(RunnerSkin skin)
    {
        if (Selected is not { } r) return;
        EndTrial();
        if (!_rewards.OwnsSkin(skin))
        {
            ShowSkinHint(skin);
            return;
        }
        WearSkin(r.Id, skin);
        _catsHint.Text = RewardCatalog.FindSkin(skin) is { } info
            ? $"{RunnerName(r)}가 {info.Name} 스킨을 입었어요"
            : $"{RunnerName(r)}가 스킨을 벗었어요";
    }

    private void WearSkin(string runnerId, RunnerSkin skin) => EditRunner(runnerId, x => x.Skin = RunnerSkins.ToId(skin));

    // ───────────────────────── 뽑기 탭 (개발용 빌드에서만) ─────────────────────────

    private Control BuildGachaPage()
    {
        var page = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };

        _stage.Finished += (_, _) => ShowPullResult();
        page.Children.Add(new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = _stage });

        // 뽑기 줄과 결과 줄은 같은 자리를 번갈아 쓴다.
        _pullTitle.FontSize = GameArt.FontTitle;
        _pullTitle.FontWeight = FontWeight.Bold;
        _pullTitle.Foreground = GameArt.Brush(GameArt.Text);
        _pullSub.FontSize = GameArt.FontBody;
        _pullSub.Foreground = GameArt.Brush(GameArt.SubText);
        _pullRow.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _pullTitle, _pullSub } });
        _gachaPrice.Margin = new Thickness(0, 0, 10, 0);
        _gachaPrice.VerticalAlignment = VerticalAlignment.Center;
        _pull.VerticalAlignment = VerticalAlignment.Center;
        _pull.Click += (_, _) => Pull();
        var pullRight = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { _gachaPrice, _pull } };
        Grid.SetColumn(pullRight, 1);
        _pullRow.Children.Add(pullRight);

        var badge = new Border
        {
            Background = GameArt.Brush(Color.FromRgb(0xD9, 0x62, 0x6B)), CornerRadius = new CornerRadius(9), Padding = new Thickness(7, 1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "NEW", FontSize = 10.5, FontWeight = FontWeight.Bold, Foreground = GameArt.Brush(Colors.White) },
        };
        _resultName.FontSize = GameArt.FontTotal;
        _resultName.FontWeight = FontWeight.Bold;
        _resultName.VerticalAlignment = VerticalAlignment.Center;
        _resultStars.FontSize = GameArt.FontBody;
        _resultStars.VerticalAlignment = VerticalAlignment.Center;
        _resultDesc.FontSize = GameArt.FontBody;
        _resultDesc.Foreground = GameArt.Brush(GameArt.SubText);
        _resultDesc.TextWrapping = TextWrapping.Wrap;
        _resultDesc.Margin = new Thickness(0, 2, 12, 0);
        var nameLine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { badge, _resultName, _resultStars } };
        _resultRow.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { nameLine, _resultDesc } });
        _again.Click += (_, _) => Pull();
        var resultRight = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { _wearButtons, _again },
        };
        Grid.SetColumn(resultRight, 1);
        _resultRow.Children.Add(resultRight);
        page.Children.Add(new CardPanel
        {
            Margin = new Thickness(0, 10, 0, 0), MinHeight = 64, Child = new Panel { Children = { _pullRow, _resultRow } },
        });

        // 도감: 스킨 3칸. 없는 칸은 실루엣과 물음표, 등급은 늘 보인다(확률은 보여 주지 않는다).
        var dexTitle = Label("도감", GameArt.FontSection, GameArt.Text, FontWeight.Bold);
        dexTitle.Margin = new Thickness(0, 12, 8, 4);
        dexTitle.VerticalAlignment = VerticalAlignment.Bottom;
        _dexCount.FontSize = GameArt.FontBody;
        _dexCount.Foreground = GameArt.Brush(GameArt.SubText);
        _dexCount.Margin = new Thickness(0, 0, 0, 5);
        _dexCount.VerticalAlignment = VerticalAlignment.Bottom;
        page.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { dexTitle, _dexCount } });

        var dex = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",8,", RewardCatalog.Skins.Select(_ => "*"))) };
        for (int i = 0; i < RewardCatalog.Skins.Count; i++)
        {
            var info = RewardCatalog.Skins[i];
            var view = new SkinRunnerView { Skin = info.Skin, Height = 54 };
            var name = new TextBlock { FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0) };
            var tier = new TextBlock { FontSize = GameArt.FontSmall, HorizontalAlignment = HorizontalAlignment.Center };
            var slot = new CardPanel { Padding = new Thickness(6), Child = new StackPanel { Children = { view, name, tier } } };
            Grid.SetColumn(slot, i * 2);
            dex.Children.Add(slot);
            _dexSlots.Add((info, view, name, tier));
        }
        page.Children.Add(dex);

        page.Children.Add(BuildGachaDevBar());
        RefreshGacha();
        return page;
    }

    /// <summary>연출을 거듭 확인할 때 쓰는 도구. 뽑기 탭 자체가 개발용이라 따로 숨기지 않는다.</summary>
    private Control BuildGachaDevBar()
    {
        _devFree.IsCheckedChanged += (_, _) => RefreshGacha();
        var addFish = new GameButton($"생선 +{GameArt.FormatCount(5000)}");
        addFish.Click += (_, _) => _rewards.DevAddFish(5000);
        var clear = new GameButton("도감 비우기");
        clear.Click += (_, _) => ClearSkinsForDev();

        var title = Label("개발용", GameArt.FontBody, Color.FromRgb(0x8A, 0x64, 0x10), FontWeight.Bold);
        var pickLabel = Label("결과", GameArt.FontBody, GameArt.Text);
        foreach (var c in new Control[] { title, pickLabel, _devPick, _devFree, addFish, clear })
        {
            c.VerticalAlignment = VerticalAlignment.Center;
            c.Margin = new Thickness(0, 2, 12, 2);
        }
        pickLabel.Margin = new Thickness(0, 2, 6, 2);
        return new Border
        {
            Background = GameArt.Brush(Color.FromRgb(0xFF, 0xF7, 0xE3)),
            BorderBrush = GameArt.Brush(Color.FromRgb(0xC9, 0x96, 0x2A)),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 4),
            Margin = new Thickness(0, 10, 0, 0),
            Child = new WrapPanel { Children = { title, pickLabel, _devPick, _devFree, addFish, clear } },
        };
    }

    /// <summary>
    /// 이 스킨이 도감·뽑기 줄에 "가진 것"으로 보이는지. 방금 뽑아 연출 중인 스킨은 아직 아니다.
    /// 무대에서 정체가 드러나기 전에 도감에 먼저 뜨면 안 되니까.
    /// </summary>
    private bool Revealed(RunnerSkin skin) => _rewards.OwnsSkin(skin) && !(_stage.IsPlaying && skin == _pulled);

    private void RefreshGacha()
    {
        if (!AppInfo.IsDev)
        {
            RefreshTeaser();
            return;
        }
        var left = RewardCatalog.Skins.Where(s => !Revealed(s.Skin)).ToList();
        bool done = left.Count == 0;
        bool free = _devFree.IsChecked == true;
        bool canPay = free || _rewards.Fish >= RewardCatalog.GachaPrice;

        _stage.Complete = done;
        _pullTitle.Text = done ? "스페셜 스킨을 모두 모았어요" : "스페셜 스킨 뽑기";
        _pullSub.Text = done ? "새 스킨이 나오면 다시 뽑을 수 있어요"
            : left.Count == RewardCatalog.Skins.Count ? string.Join(" · ", RewardCatalog.Skins.Select(s => s.Name)) + " 중 하나가 나와요"
            : $"아직 없는 스킨만 나와요 · 남은 {left.Count}종";
        _gachaPrice.IsVisible = !done;
        _gachaPrice.Affordable = canPay;
        _pull.Text = done ? "모두 모았어요" : canPay ? "한 번 뽑기" : "생선이 모자라요";
        _pull.IsEnabled = !done && canPay && !_stage.IsPlaying;
        _again.IsVisible = !done;
        _again.IsEnabled = canPay;

        foreach (var (info, view, name, tier) in _dexSlots)
        {
            bool has = Revealed(info.Skin);
            view.Owned = has;
            view.InvalidateVisual();
            string stars = new('★', info.Stars);
            name.Text = has ? info.Name : "???";
            tier.Text = has ? $"모았어요 · {stars} {info.Tier}" : $"{stars} {info.Tier}";
            tier.Foreground = GameArt.Brush(has ? GameArt.Good : GameArt.SubText);
            tier.FontWeight = has ? FontWeight.SemiBold : FontWeight.Normal;
        }
        _dexCount.Text = $"{RewardCatalog.Skins.Count}칸 중 {RewardCatalog.Skins.Count - left.Count}칸";

        // 결과 고르기 목록은 남은 스킨이 바뀔 때만 다시 만든다. 매번 만들면 고른 항목이 풀린다.
        var pickable = left.Select(s => (RunnerSkin?)s.Skin).Prepend(null).ToList();
        if (!pickable.SequenceEqual(_devPickSkins))
        {
            var keep = _devPick.SelectedIndex > 0 && _devPick.SelectedIndex < _devPickSkins.Count ? _devPickSkins[_devPick.SelectedIndex] : null;
            _devPickSkins = pickable;
            _devPick.ItemsSource = pickable.Select(s => s is { } k ? RewardCatalog.FindSkin(k)!.Name : "무작위").ToList();
            _devPick.SelectedIndex = Math.Max(0, pickable.IndexOf(keep));
        }
    }

    private void Pull()
    {
        if (_stage.IsPlaying) return;
        RunnerSkin? forced = _devPick.SelectedIndex > 0 && _devPick.SelectedIndex < _devPickSkins.Count ? _devPickSkins[_devPick.SelectedIndex] : null;
        // 뽑은 스킨은 연출 전에 바로 저장된다. 연출 중에 창을 닫아도 잃지 않는다.
        if (_rewards.TryPullSkin(forced, _devFree.IsChecked == true) is not { } skin)
        {
            RefreshGacha();
            return;
        }
        _pulled = skin;
        _pullRow.IsVisible = true;
        _resultRow.IsVisible = false;
        _stage.Play(skin);
        RefreshGacha();
    }

    private void ShowPullResult()
    {
        if (RewardCatalog.FindSkin(_pulled) is not { } info) return;
        _resultName.Text = info.Name;
        _resultStars.Text = $"{new string('★', info.Stars)}{new string('☆', 4 - info.Stars)}  {info.Tier}";
        _resultStars.Foreground = GameArt.Brush(info.Ink);
        _resultDesc.Text = info.Description;
        RefreshWearButtons();
        _pullRow.IsVisible = false;
        _resultRow.IsVisible = true;
        RefreshGacha();
        RefreshSkinTiles();
    }

    /// <summary>결과 줄의 "○○ 고양이에 입히기". 이미 입은 고양이는 "입었어요"로 잠근다.</summary>
    private void RefreshWearButtons()
    {
        _wearButtons.Children.Clear();
        foreach (var r in _working.Runners)
        {
            string id = r.Id;
            bool worn = RunnerSkins.Parse(r.Skin) == _pulled;
            var button = new GameButton(worn ? $"{RunnerName(r)}가 입었어요" : $"{RunnerName(r)}에 입히기") { IsEnabled = !worn };
            button.Click += (_, _) =>
            {
                WearSkin(id, _pulled);
                RefreshWearButtons();
            };
            _wearButtons.Children.Add(button);
        }
    }

    // ───────────────────────── 냥뽑기 예고 (일반 버전) ─────────────────────────

    /// <summary>
    /// 출시 전 예고 화면. 무대는 마우스를 올리면 실루엣이 달려와 "?"까지만 보여 주고, 버튼은 잠겨 있다.
    /// 도감 3칸은 이름·모습을 숨기고 등급만 보여 준다. 다음 릴리즈에 공개할 스킨(UpcomingSkins)만 다룬다.
    /// </summary>
    private Control BuildGachaTeaserPage()
    {
        var page = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        _stage.Teaser = true;
        page.Children.Add(new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = _stage });

        var title = Label("스페셜 스킨 뽑기", GameArt.FontTitle, GameArt.Text, FontWeight.Bold);
        title.VerticalAlignment = VerticalAlignment.Center;
        var titleLine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { title, SoonPill("곧 열려요") } };
        _teaserSub.FontSize = GameArt.FontBody;
        _teaserSub.Foreground = GameArt.Brush(GameArt.SubText);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        row.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { titleLine, _teaserSub } });
        _gachaPrice.Margin = new Thickness(0, 0, 10, 0);
        _gachaPrice.VerticalAlignment = VerticalAlignment.Center;
        var soon = new GameButton("곧 만나요!", primary: true) { MinWidth = 96, IsEnabled = false, VerticalAlignment = VerticalAlignment.Center };
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { _gachaPrice, soon } };
        Grid.SetColumn(right, 1);
        row.Children.Add(right);
        page.Children.Add(new CardPanel { Margin = new Thickness(0, 10, 0, 0), MinHeight = 64, Child = row });

        page.Children.Add(SectionHeader("도감", "어떤 고양이가 들어올지는 열리면 알 수 있어요"));
        var skins = RewardCatalog.UpcomingSkins;
        var dex = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",8,", skins.Select(_ => "*"))) };
        for (int i = 0; i < skins.Count; i++)
        {
            var info = skins[i];
            var view = new SkinRunnerView { Skin = info.Skin, Owned = false, Teaser = true, Height = 54 };
            TextBlock Line(string text, double size, Color color, FontWeight weight) => new()
            {
                Text = text, FontSize = size, Foreground = GameArt.Brush(color), FontWeight = weight, HorizontalAlignment = HorizontalAlignment.Center,
            };
            var name = Line("???", GameArt.FontBody, GameArt.Text, FontWeight.Bold);
            name.Margin = new Thickness(0, 5, 0, 0);
            var slot = new DashedCard
            {
                Child = new StackPanel
                {
                    Children =
                    {
                        view, name,
                        Line($"{new string('★', info.Stars)} {info.Tier}", GameArt.FontSmall, info.Ink, FontWeight.Normal),
                        Line("곧 만나요!", GameArt.FontSmall, GameArt.Accent, FontWeight.Bold),
                    },
                },
            };
            Grid.SetColumn(slot, i * 2);
            dex.Children.Add(slot);
        }
        page.Children.Add(dex);
        RefreshTeaser();
        return page;
    }

    /// <summary>강조색 바탕의 작은 알약 글자(곧 열려요).</summary>
    private static Border SoonPill(string text) => new()
    {
        Background = GameArt.Brush(GameArt.Accent), CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 1),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = 10, FontWeight = FontWeight.Bold, Foreground = GameArt.Brush(GameArt.OnAccent) },
    };

    /// <summary>생선이 모이면 문구가 바뀐다. 열리기 전에 한 번 뽑을 만큼 모아 두라는 목표를 준다.</summary>
    private void RefreshTeaser()
    {
        int count = RewardCatalog.UpcomingSkins.Count;
        long fish = _rewards.Fish, price = RewardCatalog.GachaPrice;
        bool enough = fish >= price;
        _teaserSub.Text = enough
            ? $"스페셜 스킨 {count}종 · 생선은 이미 넉넉해요. 열리면 바로 뽑을 수 있어요"
            : $"스페셜 스킨 {count}종 · 열리기 전에 생선 {GameArt.FormatCount(price - fish)}개를 더 모아 두세요";
        _gachaPrice.Affordable = enough;
    }

    /// <summary>개발용: 뽑은 스킨을 다 지우고, 입고 있던 스킨도 벗긴다. 없는 스킨을 입은 채로 남지 않게.</summary>
    private void ClearSkinsForDev()
    {
        if (_stage.IsPlaying) return;
        _rewards.DevClearSkins();
        bool changed = false;
        foreach (var r in _working.Runners.Where(r => r.Skin is not null))
        {
            r.Skin = null;
            changed = true;
        }
        if (changed) CommitAndRefresh();
        _stage.ShowIdle();
        _pullRow.IsVisible = true;
        _resultRow.IsVisible = false;
        RefreshGacha();
        RefreshSkinTiles();
    }

    // ───────────────────────── 옷장 탭 ─────────────────────────

    private Control BuildClosetPage()
    {
        var page = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        page.Children.Add(SectionHeader("러너 모음", "생선을 모아 새 친구를 데려오면 고양이 대신 달리게 할 수 있어요"));
        _runnerTiles.Margin = new Thickness(0, 4, 0, 0);
        _runnerTiles.MaxWidth = 680;
        _runnerTiles.HorizontalAlignment = HorizontalAlignment.Left;
        page.Children.Add(_runnerTiles);
        page.Children.Add(HintBox(_closetHint));
        return page;
    }

    private string DefaultClosetHint() =>
        Selected is { } r ? $"고양이 탭에서 고른 {RunnerName(r)}에게 입혀요." : "";

    private void RefreshCloset()
    {
        StopTeaser();
        _runnerTiles.Children.Clear();
        long fish = _rewards.Fish;
        var wearing = Selected;

        foreach (var sprite in SpriteRegistry.All)
        {
            var s = sprite;
            int price = RewardCatalog.RunnerPrice(s.Id);
            if (SpriteRegistry.IsUpcoming(s.Id))
            {
                _runnerTiles.Children.Add(UpcomingTile(s, price, wearing?.Color));
                continue;
            }
            bool owned = _rewards.OwnsRunner(s.Id);
            var tile = RunnerTile(s.DisplayName, (ctx, box, scaling) => DrawRunnerArt(ctx, box, s, wearing?.Color, scaling));
            tile.Price = price;
            tile.State = owned
                ? (string.Equals(wearing?.SpriteId, s.Id, StringComparison.OrdinalIgnoreCase) ? TileState.Equipped : TileState.Owned)
                : fish >= price ? TileState.Buyable : TileState.TooExpensive;
            tile.Click += (_, _) => OnRunnerClicked(s, price);
            tile.PointerEntered += (_, _) => _closetHint.Text = RunnerHint(s, price);
            tile.PointerExited += (_, _) => _closetHint.Text = DefaultClosetHint();
            _runnerTiles.Children.Add(tile);
        }

        // 다음에 나올 러너 자리. 그림이 생기기 전에도 목표가 보이게 한다.
        int next = RewardCatalog.NextUnreleasedRunnerPrice;
        var soon = RunnerTile("새 친구", (ctx, box, _) => DrawMystery(ctx, box));
        soon.Price = next;
        soon.State = TileState.Unavailable;
        soon.Cursor = Cursor.Default;
        soon.PointerEntered += (_, _) =>
            _closetHint.Text = $"새 러너를 준비하고 있어요. 생선 {GameArt.FormatCount(next)}개를 모아 두면 나오자마자 데려올 수 있어요";
        soon.PointerExited += (_, _) => _closetHint.Text = DefaultClosetHint();
        _runnerTiles.Children.Add(soon);
        _closetHint.Text = DefaultClosetHint();
    }

    private static ShopTile RunnerTile(string caption, Action<DrawingContext, Rect, double> art) => new()
    {
        Caption = caption, Width = 128, Height = 140, Margin = new Thickness(0, 0, 8, 8), Art = art,
    };

    /// <summary>
    /// 출시 예정 러너 칸. 그림은 보여 주되 데려올 수는 없다.
    /// 마우스를 올리면 그 자리에서 달리기 연습을 해서 어떻게 뛰는지 미리 보여 준다.
    /// </summary>
    private ShopTile UpcomingTile(IRunnerSprite sprite, int price, string? hex)
    {
        double phase = 0.4; // 멈춰 있을 때는 다른 칸과 같은 자세
        var tile = RunnerTile(sprite.DisplayName, (ctx, box, scaling) => DrawRunnerArt(ctx, box, sprite, hex, scaling, phase));
        tile.Price = price;
        tile.State = TileState.ComingSoon;
        tile.Cursor = Cursor.Default;
        tile.PointerEntered += (_, _) =>
        {
            _closetHint.Text = UpcomingTeaser(sprite, price);
            StartTeaser(() =>
            {
                // 생선이 늘면 옷장을 새로 그리면서 이 칸이 사라질 수 있다.
                if (!_runnerTiles.Children.Contains(tile)) { StopTeaser(); return; }
                phase = (phase + 0.2) % 1.0; // 5프레임을 한 칸씩
                tile.Redraw();
            });
        };
        tile.PointerExited += (_, _) =>
        {
            StopTeaser();
            phase = 0.4;
            tile.Redraw();
            _closetHint.Text = DefaultClosetHint();
        };
        tile.Click += (_, _) =>
            _closetHint.Text = $"{WithTopic(sprite.DisplayName)} 아직 연습 중이라 데려올 수 없어요. 조금만 기다려 주세요!";
        return tile;
    }

    private static string UpcomingTeaser(IRunnerSprite sprite, int price)
    {
        string practice = sprite.Id switch
        {
            "dachshund" => "닥스훈트가 짧은 다리로 열심히 달리기 연습 중이에요",
            "rabbit" => "토끼가 깡충 뛰기 연습 중이에요",
            _ => $"{sprite.DisplayName}가 달리기 연습 중이에요",
        };
        return $"{practice}. 곧 출시돼요! 생선 {GameArt.FormatCount(price)}개를 모아 두면 나오자마자 데려올 수 있어요";
    }

    /// <summary>받침이 있으면 "은", 없으면 "는".</summary>
    private static string WithTopic(string word)
    {
        char last = word[^1];
        bool batchim = last is >= '가' and <= '힣' && (last - '가') % 28 != 0;
        return word + (batchim ? "은" : "는");
    }

    private void StartTeaser(Action step)
    {
        _teaserStep = step;
        _teaserTimer.Start();
    }

    private void StopTeaser()
    {
        _teaserTimer.Stop();
        _teaserStep = null;
    }

    private static void DrawRunnerArt(DrawingContext ctx, Rect box, IRunnerSprite sprite, string? hex, double scaling, double phase = 0.4)
    {
        var color = ColorUtil.Parse(hex, Colors.Black);
        GameArt.FillRound(ctx, box, 8, GameArt.SwatchBackFor(color));
        var size = sprite.Measure(Math.Max(4, box.Height * 0.55));
        var catBox = new Rect(box.X + (box.Width - size.Width) / 2, box.Y + (box.Height - size.Height) / 2, size.Width, size.Height);
        sprite.Draw(ctx, catBox, new RunnerPose(1, phase, 0.5), color, scaling);
    }

    private static void DrawMystery(DrawingContext ctx, Rect box)
    {
        GameArt.FillRound(ctx, box, 8, GameArt.Mystery);
        GameArt.DrawCentered(ctx, GameArt.Format("?", 34.7, GameArt.SubText, FontWeight.Bold), box);
    }

    private string RunnerHint(IRunnerSprite sprite, int price)
    {
        if (_rewards.OwnsRunner(sprite.Id))
        {
            return string.Equals(Selected?.SpriteId, sprite.Id, StringComparison.OrdinalIgnoreCase)
                ? $"{sprite.DisplayName} — {(Selected is { } r ? RunnerName(r) : "")}로 달리고 있어요"
                : $"{sprite.DisplayName} — 눌러서 {(Selected is { } r2 ? MetricName(r2.Metric) : "")} 러너로 바꿔요";
        }
        return LockedHint(sprite.DisplayName, price, "눌러서 데려와요");
    }

    private async void OnRunnerClicked(IRunnerSprite sprite, int price)
    {
        if (Selected is not { } r) return;
        if (!_rewards.OwnsRunner(sprite.Id))
        {
            if (_rewards.Fish < price)
            {
                _closetHint.Text = RunnerHint(sprite, price);
                return;
            }
            if (!await ConfirmPurchaseAsync($"{sprite.DisplayName}를 생선 {GameArt.FormatCount(price)}개로 데려올까요?", price)) return;
            if (!_rewards.TryBuyRunner(sprite.Id)) return;
        }
        EditRunner(r.Id, x => x.SpriteId = sprite.Id);
        RefreshCloset();
    }

    // ───────────────────────── 기록 탭 ─────────────────────────

    private Control BuildProfilePage()
    {
        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,*,Auto"),
            Margin = new Thickness(8, 14, 8, 4),
        };

        void Add(Control c, int row)
        {
            Grid.SetRow(c, row);
            layout.Children.Add(c);
        }

        _todayHeadline.FontSize = GameArt.FontHeadline;
        _todayHeadline.FontWeight = FontWeight.Bold;
        _todayHeadline.Margin = new Thickness(0, 0, 0, 2);
        Add(_todayHeadline, 0);

        _todayCompare.FontSize = GameArt.FontLarge;
        _todayCompare.Foreground = GameArt.Brush(GameArt.SubText);
        _todayCompare.Margin = new Thickness(2, 0, 0, 14);
        Add(_todayCompare, 1);

        _runnerStats.ColumnDefinitions = new ColumnDefinitions("52,*,110,110");
        Add(new CardPanel { Height = 150, Child = _runnerStats }, 2);

        _totalLine.FontSize = GameArt.FontTotal;
        _totalLine.FontWeight = FontWeight.Bold;
        _totalLine.Margin = new Thickness(2, 18, 0, 2);
        Add(_totalLine, 3);

        _totalCompare.FontSize = GameArt.FontLarge;
        _totalCompare.Foreground = GameArt.Brush(GameArt.SubText);
        _totalCompare.Margin = new Thickness(2, 0, 0, 0);
        Add(_totalCompare, 4);

        _fishLine.FontSize = GameArt.FontLarge;
        _fishLine.Foreground = GameArt.Brush(GameArt.FishDark);
        _fishLine.Margin = new Thickness(2, 14, 0, 0);
        Add(_fishLine, 5);

        _pitchNote.FontSize = GameArt.FontNote;
        _pitchNote.Foreground = GameArt.Brush(GameArt.SubText);
        _pitchNote.Margin = new Thickness(2, 8, 0, 0);
        Add(_pitchNote, 7);
        return layout;
    }

    /// <summary>러너 구성(색·표시 여부)이 바뀌었을 수 있으니 탭을 열 때마다 표를 새로 만든다. 숫자는 RefreshProfile이 채운다.</summary>
    private void BuildRunnerStatRows()
    {
        _runnerStats.Children.Clear();
        _runnerStatLabels.Clear();

        // 줄 높이를 고정하고 남는 높이는 맨 아래 빈 줄에 몰아준다.
        _runnerStats.RowDefinitions = new RowDefinitions("24," + string.Concat(_working.Runners.Select(_ => "40,")) + "*");
        Place(StatHeader("오늘"), 0, 2);
        Place(StatHeader("지금까지"), 0, 3);

        int row = 1;
        foreach (var r in _working.Runners)
        {
            Place(new RunnerIcon(r) { Width = 48, Height = 34, HorizontalAlignment = HorizontalAlignment.Left }, row, 0);
            Place(StatCell(RunnerName(r) + (r.Enabled ? "" : " (숨김)"), TextAlignment.Left), row, 1);
            var today = StatCell("", TextAlignment.Right);
            var total = StatCell("", TextAlignment.Right);
            Place(today, row, 2);
            Place(total, row, 3);
            _runnerStatLabels[r.Id] = (today, total);
            row++;
        }

        void Place(Control c, int r, int col)
        {
            Grid.SetRow(c, r);
            Grid.SetColumn(c, col);
            _runnerStats.Children.Add(c);
        }
    }

    private static TextBlock StatHeader(string text) => new()
    {
        Text = text, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
        FontSize = GameArt.FontBody, Foreground = GameArt.Brush(GameArt.SubText),
    };

    private static TextBlock StatCell(string text, TextAlignment align) => new()
    {
        Text = text, TextAlignment = align, VerticalAlignment = VerticalAlignment.Center,
        FontSize = GameArt.FontSection, Foreground = GameArt.Brush(GameArt.Text),
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

        if (ScreenCatalog.Resolve(Screens, _working.MonitorDeviceName) is not { } screen)
        {
            _pitchNote.Text = "";
            return;
        }
        var pitch = MonitorPitch.For(screen);
        _pitchNote.Text = pitch.Measured
            ? $"{pitch.MonitorName ?? "모니터"} 실제 크기({pitch.WidthMm} × {pitch.HeightMm} mm)로 계산했어요 · 1px = {pitch.MillimetersPerPixel:0.000} mm"
            : $"모니터 크기를 알 수 없어 화면 배율로 추정했어요 · 1px ≈ {pitch.MillimetersPerPixel:0.000} mm";
    }

    // ───────────────────────── 일반 탭 ─────────────────────────

    private Control BuildGeneralPage()
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
        };

        var label = Label("달릴 모니터", GameArt.FontBody, GameArt.Text, FontWeight.Bold);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Margin = new Thickness(0, 0, 16, 0);
        grid.Children.Add(label);

        _monitor.Width = 380;
        _monitor.SelectionChanged += (_, _) =>
        {
            if (_loading || _monitor.SelectedIndex < 0 || _monitor.SelectedIndex >= _monitorItems.Count) return;
            _working.MonitorDeviceName = _monitorItems[_monitor.SelectedIndex].Key;
            Commit();
        };
        var refresh = new GameButton("다시 찾기") { Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        refresh.Click += (_, _) => LoadMonitors();
        var monitorRow = new StackPanel { Orientation = Orientation.Horizontal, Children = { _monitor, refresh } };
        Grid.SetColumn(monitorRow, 1);
        grid.Children.Add(monitorRow);

        // 전체 화면 앱을 알아챌 수 없는 OS(macOS)에서는 옵션을 숨긴다. 그곳에서는 전체 화면 앱이 따로 데스크톱을 써서 알아서 가려진다.
        _hideFullScreen.Content = "전체 화면 앱(게임·발표) 실행 중에는 숨기기";
        _hideFullScreen.Margin = new Thickness(0, 10, 0, 0);
        _hideFullScreen.IsVisible = _platform.CanDetectFullScreen;
        _hideFullScreen.IsCheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _working.HideInFullScreen = _hideFullScreen.IsChecked == true;
            Commit();
        };
        Grid.SetRow(_hideFullScreen, 1);
        Grid.SetColumn(_hideFullScreen, 1);
        grid.Children.Add(_hideFullScreen);

        var card = new CardPanel { Padding = new Thickness(16, 14, 16, 12), Child = grid, VerticalAlignment = VerticalAlignment.Top };
        return new StackPanel { Margin = new Thickness(0, 12, 0, 0), Children = { card } };
    }

    // ───────────────────────── 데이터 ↔ 화면 ─────────────────────────

    private void LoadAll()
    {
        _loading = true;
        try
        {
            if (_working.Runners.All(r => r.Id != _selectedId)) _selectedId = _working.Runners.FirstOrDefault()?.Id ?? "cpu";
            BuildRunnerCards();
            _hideFullScreen.IsChecked = _working.HideInFullScreen;
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
        // 기록을 보는 중에 표시 여부가 바뀌면 표의 "(숨김)"도 따라가게 한다.
        if (_page == Page.Profile) BuildRunnerStatRows();
        if (_page == Page.Closet) RefreshCloset();
    }

    private void LoadMonitors()
    {
        bool wasLoading = _loading;
        _loading = true;
        try
        {
            _monitorItems = ScreenCatalog.List(Screens).ToList();
            _monitor.ItemsSource = _monitorItems.Select(m => m.Label).ToList();
            // 저장된 모니터가 지금 없으면(분리됨) 실제로 쓰이는 주 모니터를 선택해 보여준다.
            string? current = ScreenCatalog.Resolve(Screens, _working.MonitorDeviceName) is { } s ? _platform.ScreenKey(s) : null;
            int index = _monitorItems.FindIndex(m => m.Key == current);
            _monitor.SelectedIndex = index >= 0 ? index : _monitorItems.Count > 0 ? 0 : -1;
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
        if (_page == Page.Gacha) RefreshGacha();
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
        // 내가 방금 저장한 값이면 화면과 같으므로, 창 밖에서 바뀔 수 있는 값(표시 여부, 개발용 트레이의 스킨)만
        // 비교해서 다를 때만 다시 읽는다. 다시 읽지 않으면 이 창이 들고 있던 옛 값으로 다음 저장 때 덮어쓴다.
        var current = _store.Current;
        bool differs = current.Runners.Count != _working.Runners.Count ||
                       current.Runners.Zip(_working.Runners).Any(p =>
                           p.First.Enabled != p.Second.Enabled || p.First.Skin != p.Second.Skin);
        if (!differs) return;
        _working = current.Clone();
        LoadAll();
    }

    private void OnRewardsChanged(object? sender, EventArgs e)
    {
        _lastFish = -1; // 생선을 썼으니 지갑과 칸 상태를 바로 다시 그린다
        RefreshWallet();
        RefreshCards();
        RefreshGacha();
    }

    public static string RunnerName(RunnerConfig r) => $"{MetricName(r.Metric)} {SpriteRegistry.Get(r.SpriteId).DisplayName}";

    public static string MetricName(MetricKind m) => m switch
    {
        MetricKind.Cpu => "CPU",
        MetricKind.Memory => "메모리",
        _ => m.ToString(),
    };
}
