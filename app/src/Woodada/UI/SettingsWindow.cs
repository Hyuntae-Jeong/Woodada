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
/// 옷장 — 러너 모음. 기록 — 달린 거리. 일반 — 모니터 · 전체 화면 · 메뉴 막대 생선 수 · 언어.
/// 냥뽑기 — 생선으로 스페셜 스킨 뽑기(꽝 있음). 뽑은 스킨은 고양이 탭 맨 아래 스킨 줄에서 입힌다.
/// 개발용 빌드는 아직 공개하지 않은 스킨까지 보이고, 뽑기 탭에 개발용 도구가 붙는다.
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
    private readonly GameButton _trialRepick = new(Loc.T("다른 색", "Other color"));
    private readonly GameButton _trialCancel = new(Loc.T("그만", "Stop"));
    private Trial? _trial;

    /// <summary>사기 전에 입어 보는 색. Preset이 없으면 나만의 색 칸(CustomIndex)이다.</summary>
    private sealed record Trial(string Hex, string Label, int Price, PresetColor? Preset, int CustomIndex);

    // 고양이 탭: 스페셜 스킨 줄
    private readonly List<(RunnerSkin Skin, ShopTile Tile)> _skinTiles = new();
    // 스킨 칸 고양이가 제자리 달리기를 하도록 다시 그리는 타이머. 고양이 탭을 볼 때만 일한다.
    private readonly DispatcherTimer _skinTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly GameButton _gotoGacha = new(Loc.T("냥뽑기로 가기", "Go to Gacha")) { IsVisible = false, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };

    // 냥뽑기 탭
    private readonly GachaStage _stage = new() { Height = 210 };
    private readonly Grid _pullRow = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
    private readonly TextBlock _pullTitle = new();
    private readonly TextBlock _pullSub = new();
    private readonly PriceLabel _gachaPrice = new() { Price = RewardCatalog.GachaPrice };
    private readonly GameButton _pull = new(Loc.T("한 번 뽑기", "Pull once"), primary: true) { MinWidth = 96 };
    private readonly Grid _resultRow = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), IsVisible = false };
    private readonly TextBlock _resultName = new();
    private readonly TextBlock _resultStars = new();
    private readonly TextBlock _resultDesc = new();
    private readonly StackPanel _wearButtons = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    private readonly GameButton _again = new(Loc.T("한 번 더", "Again"), primary: true);
    private readonly TextBlock _dexCount = new();
    private readonly List<(SkinInfo Info, SkinRunnerView View, TextBlock Name, TextBlock Tier)> _dexSlots = new();
    private readonly ComboBox _devPick = new() { MinWidth = 110 };
    private List<RunnerSkin?> _devPickSkins = new();
    private readonly CheckBox _devFree = new() { Content = Loc.T("생선 안 쓰고 뽑기", "Pull without fish") };
    /// <summary>방금 뽑아 연출 중이거나 결과를 보여 주는 스킨. None이면 꽝이다.</summary>
    private RunnerSkin _pulled;
    /// <summary>뽑기 직전의 꽝 연속 횟수. 연출 중에는 이 값을 보여 줘야 결과가 미리 새지 않는다.</summary>
    private int _streakBeforePull;
    /// <summary>방금 꽝으로 돌려받은 생선.</summary>
    private int _pullRefund;
    /// <summary>
    /// 생선 띠에 아직 더하지 않은 돌려받은 생선. 저장은 뽑자마자 하지만, 띠의 "+n"은 무대에서 생선이 날아간 뒤에 보여 준다.
    /// 먼저 늘면 무대보다 먼저 꽝인 걸 알려 버린다.
    /// </summary>
    private int _heldRefund;
    private readonly Border _resultBadge = new()
    {
        CornerRadius = new CornerRadius(9), Padding = new Thickness(7, 1), VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock _resultBadgeText = new() { FontSize = 10.5, FontWeight = FontWeight.Bold, Foreground = GameArt.Brush(Colors.White) };
    private static readonly Color NewBadge = Color.FromRgb(0xD9, 0x62, 0x6B);
    private static readonly Color MissBadge = Color.FromRgb(0x8A, 0x93, 0xA1);

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
    private readonly CheckBox _menuBarFish = new();
    private readonly ComboBox _language = new() { Width = 200 };

    /// <summary>언어 고르기 목록. 각 언어는 제 말로 적어야 못 읽는 언어로 바뀌었을 때도 돌아올 수 있다.</summary>
    private static readonly (AppLanguage Language, string Label)[] Languages =
    {
        (AppLanguage.English, "English"),
        (AppLanguage.Korean, "한국어"),
    };

    public SettingsWindow(SettingsStore store, StatsStore stats, RewardStore rewards, MetricSampler sampler)
    {
        _store = store;
        _stats = stats;
        _rewards = rewards;
        _sampler = sampler;
        _working = store.Current.Clone();
        _selectedId = _working.Runners.FirstOrDefault()?.Id ?? "cpu";

        Title = AppInfo.IsDev ? Loc.T("Woodada (개발용)", "Woodada (Dev)") : "Woodada";
        Icon = AppIcon.Window;
        Width = 700;
        // 고양이 탭 맨 아래 스페셜 스킨 줄까지 들어가는 높이.
        Height = 804;
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
        _skinTimer.Start();
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
        if (page != Page.Gacha && _heldRefund > 0)
        {
            // 연출 도중 다른 탭으로 가면 무대가 멈춘다. 돌려받은 생선을 띠에 바로 더해서 다른 탭의 생선 수와 맞춘다.
            _heldRefund = 0;
            RefreshWallet();
        }
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

        AddPage(nav, Page.Cats, Loc.T("고양이", "Cats"), BuildCatsPage());
        AddPage(nav, Page.Closet, Loc.T("옷장", "Closet"), BuildClosetPage());
        AddPage(nav, Page.Profile, Loc.T("기록", "Stats"), BuildProfilePage());
        AddPage(nav, Page.General, Loc.T("일반", "General"), BuildGeneralPage());
        AddPage(nav, Page.Gacha, Loc.T("냥뽑기", "Cat Gacha"), BuildGachaPage());

        var close = new GameButton(Loc.T("닫기", "Close")) { MinWidth = 80, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 6, 12, 12) };
        close.Click += (_, _) => Close();
        Grid.SetRow(close, 3);
        root.Children.Add(close);
    }

    private void AddPage(StackPanel nav, Page page, string title, Control view)
    {
        var pill = new NavPill(title);
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

        page.Children.Add(SectionHeader(Loc.T("기본 색", "Basic colors"), Loc.T("까망·하양은 처음부터 있어요. 나머지는 생선으로 열어요", "Black and White are free. Unlock the rest with fish")));
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

        page.Children.Add(SectionHeader(Loc.T("나만의 색", "My colors"), Loc.T("직접 고른 색을 담아요. 앞 칸부터 열리고, 10칸을 다 열면 색을 바꿀 수 있어요", "Colors you pick yourself. Slots open in order; open all 10 to recolor them")));
        var customs = TileRow();
        for (int i = 0; i < RewardCatalog.CustomSlotCount; i++)
        {
            int index = i;
            var tile = ColorTile(Loc.T($"{i + 1}번 칸", $"Slot {i + 1}"), null);
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
        page.Children.Add(BuildSkinRow());
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
        return i >= 0 ? Loc.T($"나만의 색 {i + 1}", $"My color {i + 1}") : hex.ToUpperInvariant();
    }

    private string DefaultCatsHint() =>
        Selected is { } r
            ? Loc.T($"{RunnerName(r)}에게 입힐 색과 스킨을 골라요. 위 카드를 누르면 다른 고양이를 고를 수 있어요.", $"Pick a color and skin for the {RunnerName(r)}. Click a card above to switch cats.")
            : "";

    private string PresetHint(PresetColor c)
    {
        if (_rewards.Owns(c))
            return SameColor(c.Hex, Selected?.Color)
                ? Loc.T($"{c.Name} — 지금 입고 있어요", $"{c.Name} — wearing it now")
                : Loc.T($"{c.Name} — 눌러서 입혀요", $"{c.Name} — click to wear");
        return LockedHint(c.Name, c.Price, Loc.T("눌러서 입어 보고 살 수 있어요", "Click to try it on and buy"));
    }

    private string CustomHint(int index)
    {
        string name = Loc.T($"나만의 색 {index + 1}", $"My color {index + 1}");
        int opened = _rewards.CustomSlotsOpened;
        if (index < opened)
        {
            bool wearing = SameColor(_rewards.Current.CustomColors[index], Selected?.Color);
            if (!_rewards.CanRecolorCustomSlots)
                return wearing ? Loc.T($"{name} — 입고 있어요", $"{name} — wearing it") : Loc.T($"{name} — 눌러서 입혀요", $"{name} — click to wear");
            return wearing
                ? Loc.T($"{name} — 입고 있어요. 한 번 더 누르면 색을 바꿀 수 있어요", $"{name} — wearing it. Click again to change its color")
                : Loc.T($"{name} — 눌러서 입혀요. 오른쪽 클릭하면 색을 바꿔요", $"{name} — click to wear, right-click to change its color");
        }
        string price = GameArt.FormatCount(RewardCatalog.CustomSlotPrice(index));
        if (index > opened) return Loc.T($"{name} — 앞 칸을 먼저 열어야 해요 (생선 {price}개)", $"{name} — open the slot before it first ({price} fish)");
        return LockedHint(name, RewardCatalog.CustomSlotPrice(index), Loc.T("눌러서 색을 고르면 입어 보고 살 수 있어요", "Click to pick a color, try it on, and buy"));
    }

    private string LockedHint(string name, int price, string action)
    {
        long fish = _rewards.Fish;
        string cost = Loc.T($"{name} — 생선 {GameArt.FormatCount(price)}개", $"{name} — {GameArt.FormatCount(price)} fish");
        string more = GameArt.FormatCount(price - fish);
        return fish >= price ? $"{cost}. {action}"
            : Loc.T($"{cost}. {more}개 더 모으면 열 수 있어요 (입어 보기는 지금도 돼요)", $"{cost}. {more} more to unlock (you can still try it on)");
    }

    private void OnPresetClicked(PresetColor color)
    {
        if (Selected is not { } r) return;
        if (_rewards.Owns(color))
        {
            _trial = null; // 다른 칸을 누르면 입어 보던 건 내려놓는다
            Wear(color.Hex);
            UpdateTrialBar();
            _catsHint.Text = Loc.T($"{RunnerName(r)}가 {color.Name} 색을 입었어요", $"The {RunnerName(r)} is now wearing {color.Name}");
            return;
        }
        StartTrial(new Trial(color.Hex, Loc.T($"{color.Name} 색", color.Name), color.Price, color, -1));
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
                    _catsHint.Text = Loc.T($"나만의 색 10칸을 모두 열면 칸의 색을 바꿀 수 있어요 (지금 {opened}/{RewardCatalog.CustomSlotCount}칸)",
                        $"Open all 10 of My colors to recolor slots (now {opened}/{RewardCatalog.CustomSlotCount})");
                    return;
                }
                if (await PickColorAsync(hex) is { } picked) Recolor(index, hex, picked);
                return;
            }
            Wear(hex);
            _catsHint.Text = Loc.T($"{RunnerName(r)}가 나만의 색 {index + 1}을 입었어요", $"The {RunnerName(r)} is now wearing My color {index + 1}");
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
        StartTrial(new Trial(chosen, Loc.T("고른 색", "the color you picked"), RewardCatalog.CustomSlotPrice(index), null, index));
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
            ? Loc.T($"생선 {GameArt.FormatCount(t.Price)}개로 사기", $"Buy for {GameArt.FormatCount(t.Price)} fish")
            : Loc.T($"생선 {GameArt.FormatCount(t.Price - fish)}개 부족", $"{GameArt.FormatCount(t.Price - fish)} fish short");
        _trialRepick.IsVisible = t.Preset is null;
        string what = t.Preset is null ? Loc.T($"{t.CustomIndex + 1}번 칸에 담을 {t.Label}", $"{t.Label} for slot {t.CustomIndex + 1}") : t.Label;
        _catsHint.Text = Loc.T($"{RunnerName(r)}가 {what}을 입어 보는 중이에요", $"The {RunnerName(r)} is trying on {what}");
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
            ? Loc.T($"{p.Name} 색을 얻었어요! {RunnerName(r)}가 입었어요", $"Got {p.Name}! The {RunnerName(r)} is wearing it")
            : Loc.T($"나만의 색 {t.CustomIndex + 1}번 칸을 열었어요! {RunnerName(r)}가 입었어요", $"Opened My color slot {t.CustomIndex + 1}! The {RunnerName(r)} is wearing it");
    }

    private Task<bool> ConfirmPurchaseAsync(string question, int price) =>
        Dialogs.ConfirmAsync(this, Loc.T("잠금 해제", "Unlock"),
            Loc.T($"{question}\n\n남는 생선: {GameArt.FormatCount(_rewards.Fish - price)}개", $"{question}\n\nFish left: {GameArt.FormatCount(_rewards.Fish - price)}"));

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

    // ───────── 고양이 탭: 스페셜 스킨 줄 ─────────

    /// <summary>
    /// 기본 모습 + 스킨들. 가진 칸은 고른 고양이 색으로 제자리 달리기를 하고, 없는 칸은 실루엣과 물음표다.
    /// 스킨이 늘어도 한 줄에 들어오게 칸 너비를 나눠 갖는다.
    /// </summary>
    private Control BuildSkinRow()
    {
        var skins = RewardCatalog.AvailableSkins.Select(s => s.Skin).Prepend(RunnerSkin.None).ToList();
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
            Children = { SectionHeader(Loc.T("스페셜 스킨", "Special skins"), Loc.T("뽑기로 얻어요. 색은 그대로 두고 겉모습만 바뀌어요", "Win them in the gacha. They change the look and keep your color")), row },
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
            tile.Caption = skin == RunnerSkin.None ? Loc.T("기본 모습", "Default") : owned ? RewardCatalog.FindSkin(skin)!.Name : "???";
            tile.LockedNote = owned ? null : Loc.T("뽑기에서 얻어요", "From the gacha");
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
            _catsHint.Text = wearing
                ? Loc.T("기본 모습 — 지금 스킨 없이 달리고 있어요", "Default — running without a skin")
                : Loc.T("기본 모습 — 눌러서 스킨을 벗어요", "Default — click to take the skin off");
            return;
        }
        if (!_rewards.OwnsSkin(skin))
        {
            _catsHint.Text = Loc.T($"아직 없는 스킨이에요. 뽑기에서 생선 {GameArt.FormatCount(RewardCatalog.GachaPrice)}개로 뽑을 수 있어요",
                $"You don't have this skin yet. Try the gacha for {GameArt.FormatCount(RewardCatalog.GachaPrice)} fish");
            _gotoGacha.IsVisible = true;
            return;
        }
        var info = RewardCatalog.FindSkin(skin)!;
        _catsHint.Text = wearing
            ? Loc.T($"{info.Name} — 지금 입고 있어요", $"{info.Name} — wearing it now")
            : Loc.T($"{info.Name} — 눌러서 {RunnerName(r)}에게 입혀요", $"{info.Name} — click to put it on the {RunnerName(r)}");
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
            ? Loc.T($"{RunnerName(r)}가 {info.Name} 스킨을 입었어요", $"The {RunnerName(r)} is now wearing {info.Name}")
            : Loc.T($"{RunnerName(r)}가 스킨을 벗었어요", $"The {RunnerName(r)} took its skin off");
    }

    private void WearSkin(string runnerId, RunnerSkin skin) => EditRunner(runnerId, x => x.Skin = RunnerSkins.ToId(skin));

    // ───────────────────────── 냥뽑기 탭 ─────────────────────────

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

        // 당첨이면 빨간 "NEW", 꽝이면 회색 "꽝".
        _resultBadge.Child = _resultBadgeText;
        _resultName.FontSize = GameArt.FontTotal;
        _resultName.FontWeight = FontWeight.Bold;
        _resultName.VerticalAlignment = VerticalAlignment.Center;
        _resultStars.FontSize = GameArt.FontBody;
        _resultStars.VerticalAlignment = VerticalAlignment.Center;
        _resultDesc.FontSize = GameArt.FontBody;
        _resultDesc.Foreground = GameArt.Brush(GameArt.SubText);
        _resultDesc.TextWrapping = TextWrapping.Wrap;
        _resultDesc.Margin = new Thickness(0, 2, 12, 0);
        var nameLine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _resultBadge, _resultName, _resultStars } };
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
        var dexTitle = Label(Loc.T("도감", "Collection"), GameArt.FontSection, GameArt.Text, FontWeight.Bold);
        dexTitle.Margin = new Thickness(0, 12, 8, 4);
        dexTitle.VerticalAlignment = VerticalAlignment.Bottom;
        _dexCount.FontSize = GameArt.FontBody;
        _dexCount.Foreground = GameArt.Brush(GameArt.SubText);
        _dexCount.Margin = new Thickness(0, 0, 0, 5);
        _dexCount.VerticalAlignment = VerticalAlignment.Bottom;
        page.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { dexTitle, _dexCount } });

        var skinsInDex = RewardCatalog.AvailableSkins;
        var dex = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",8,", skinsInDex.Select(_ => "*"))) };
        for (int i = 0; i < skinsInDex.Count; i++)
        {
            var info = skinsInDex[i];
            var view = new SkinRunnerView { Skin = info.Skin, Height = 54 };
            var name = new TextBlock { FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0) };
            var tier = new TextBlock { FontSize = GameArt.FontSmall, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center };
            var slot = new CardPanel { Padding = new Thickness(6), Child = new StackPanel { Children = { view, name, tier } } };
            Grid.SetColumn(slot, i * 2);
            dex.Children.Add(slot);
            _dexSlots.Add((info, view, name, tier));
        }
        page.Children.Add(dex);

        if (AppInfo.IsDev) page.Children.Add(BuildGachaDevBar());
        RefreshGacha();
        return page;
    }

    /// <summary>연출을 거듭 확인할 때 쓰는 도구. 개발용 빌드에만 붙는다.</summary>
    private Control BuildGachaDevBar()
    {
        _devFree.IsCheckedChanged += (_, _) => RefreshGacha();
        var addFish = new GameButton(Loc.T($"생선 +{GameArt.FormatCount(5000)}", $"Fish +{GameArt.FormatCount(5000)}"));
        addFish.Click += (_, _) => _rewards.DevAddFish(5000);
        var clear = new GameButton(Loc.T("도감 비우기", "Clear collection"));
        clear.Click += (_, _) => ClearSkinsForDev();

        var title = Label(Loc.T("개발용", "Dev"), GameArt.FontBody, Color.FromRgb(0x8A, 0x64, 0x10), FontWeight.Bold);
        var pickLabel = Label(Loc.T("결과", "Result"), GameArt.FontBody, GameArt.Text);
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
        var all = RewardCatalog.AvailableSkins;
        var left = all.Where(s => !Revealed(s.Skin)).ToList();
        bool done = left.Count == 0;
        bool free = _devFree.IsChecked == true;
        bool canPay = free || _rewards.Fish >= RewardCatalog.GachaPrice;

        _stage.Complete = done;
        _pullTitle.Text = done ? Loc.T("스페셜 스킨을 모두 모았어요", "You've collected every special skin") : Loc.T("스페셜 스킨 뽑기", "Special skin gacha");
        _pullSub.Text = done ? Loc.T("새 스킨이 나오면 다시 뽑을 수 있어요", "You can pull again when a new skin comes out")
            : (left.Count == all.Count
                ? Loc.T($"스킨 {left.Count}종 중 하나, 아니면 꽝이 나와요", $"One of {left.Count} skins, or a miss")
                : Loc.T($"아직 없는 스킨 {left.Count}종, 아니면 꽝이 나와요", $"One of the {left.Count} skins you don't have, or a miss"))
              + PityNote();
        _gachaPrice.IsVisible = !done;
        _gachaPrice.Affordable = canPay;
        _pull.Text = done ? Loc.T("모두 모았어요", "All collected") : canPay ? Loc.T("한 번 뽑기", "Pull once") : Loc.T("생선이 모자라요", "Not enough fish");
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
            // 한 줄로 쓰면 "Collected · ★★★★ Legendary"가 칸 폭을 넘어 잘리므로 두 줄로 나눈다.
            tier.Text = has ? Loc.T($"모았어요\n{stars} {info.Tier}", $"Collected\n{stars} {info.Tier}") : $"{stars} {info.Tier}";
            tier.Foreground = GameArt.Brush(has ? GameArt.Good : GameArt.SubText);
            tier.FontWeight = has ? FontWeight.SemiBold : FontWeight.Normal;
        }
        _dexCount.Text = Loc.T($"{all.Count}칸 중 {all.Count - left.Count}칸", $"{all.Count - left.Count} of {all.Count}");

        // 결과 고르기: 무작위 · 꽝(None) · 남은 스킨. 남은 스킨이 바뀔 때만 다시 만든다. 매번 만들면 고른 항목이 풀린다.
        var pickable = left.Select(s => (RunnerSkin?)s.Skin).Prepend(RunnerSkin.None).Prepend(null).ToList();
        if (!pickable.SequenceEqual(_devPickSkins))
        {
            var keep = _devPick.SelectedIndex > 0 && _devPick.SelectedIndex < _devPickSkins.Count ? _devPickSkins[_devPick.SelectedIndex] : null;
            _devPickSkins = pickable;
            _devPick.ItemsSource = pickable.Select(s => s switch
            {
                null => Loc.T("무작위", "Random"),
                RunnerSkin.None => Loc.T("꽝", "Miss"),
                { } k => RewardCatalog.FindSkin(k)!.Name,
            }).ToList();
            _devPick.SelectedIndex = Math.Max(0, pickable.IndexOf(keep));
        }
    }

    /// <summary>
    /// 꽝이 이어지고 있으면 천장까지 남은 횟수. 확률이 아니라 약속이라 보여 준다.
    /// 연출 중에는 뽑기 전 값을 보여 준다. 바로 바뀌면 "꽝 1번"이 무대보다 먼저 결과를 알려 버린다.
    /// </summary>
    private string PityNote()
    {
        int streak = _stage.IsPlaying ? _streakBeforePull : _rewards.Current.GachaMissStreak;
        if (streak <= 0) return "";
        int until = Math.Max(1, RewardCatalog.GachaPity + 1 - streak);
        string misses = streak == 1 ? "1 miss" : $"{streak} misses";
        return until <= 1
            ? Loc.T($" · 꽝 {streak}번, 다음엔 꼭 나와요", $" · {misses}, the next one is guaranteed")
            : Loc.T($" · 꽝 {streak}번, {until}번 안에 꼭 나와요", $" · {misses}, guaranteed within {until}");
    }

    private void Pull()
    {
        if (_stage.IsPlaying) return;
        RunnerSkin? forced = _devPick.SelectedIndex > 0 && _devPick.SelectedIndex < _devPickSkins.Count ? _devPickSkins[_devPick.SelectedIndex] : null;
        _streakBeforePull = _rewards.Current.GachaMissStreak;
        // 결과(스킨이든 꽝이든)는 연출 전에 바로 저장된다. 연출 중에 창을 닫아도 잃지 않는다.
        if (_rewards.TryPull(forced, _devFree.IsChecked == true) is not { } result)
        {
            RefreshGacha();
            return;
        }
        var skin = result.Skin;
        _pulled = skin;
        _pullRefund = result.Refund;
        _heldRefund = result.Refund;
        RefreshWallet();
        _pullRow.IsVisible = true;
        _resultRow.IsVisible = false;
        _stage.Play(skin == RunnerSkin.None ? null : skin, result.Refund);
        RefreshGacha();
    }

    private void ShowPullResult()
    {
        // 무대의 생선이 다 날아갔다(또는 건너뛰었다). 이제 생선 띠에 "+n"으로 더한다.
        _heldRefund = 0;
        RefreshWallet();
        _pullRow.IsVisible = false;
        _resultRow.IsVisible = true;
        if (RewardCatalog.FindSkin(_pulled) is not { } info)
        {
            // 꽝: 회색 배지와 무대 문구, 천장까지 남은 횟수.
            _resultBadge.Background = GameArt.Brush(MissBadge);
            _resultBadgeText.Text = Loc.T("꽝", "MISS");
            _resultName.Text = Loc.T("앗, 도망갔어요!", "Oops, it got away!");
            _resultStars.Text = "";
            int until = _rewards.PullsUntilGuaranteed;
            string price = GameArt.FormatCount(RewardCatalog.GachaPrice);
            string spent = _pullRefund > 0
                ? Loc.T($"생선 {price}개를 쓰고 {GameArt.FormatCount(_pullRefund)}개를 돌려받았어요.",
                    $"Spent {price} fish, got {GameArt.FormatCount(_pullRefund)} back.")
                : Loc.T($"생선 {price}개를 썼어요.", $"Spent {price} fish.");
            _resultDesc.Text = until <= 1
                ? Loc.T($"{spent} 다음 번엔 꼭 스킨이 나와요", $"{spent} Your next pull is guaranteed a skin")
                : Loc.T($"{spent} {until}번 안에 꼭 스킨이 나와요", $"{spent} A skin is guaranteed within {until} pulls");
            _wearButtons.Children.Clear();
            RefreshGacha();
            return;
        }
        _resultBadge.Background = GameArt.Brush(NewBadge);
        _resultBadgeText.Text = "NEW";
        _resultName.Text = info.Name;
        _resultStars.Text = $"{new string('★', info.Stars)}{new string('☆', 4 - info.Stars)}  {info.Tier}";
        _resultStars.Foreground = GameArt.Brush(info.Ink);
        _resultDesc.Text = info.Description;
        RefreshWearButtons();
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
            var button = new GameButton(worn
                ? Loc.T($"{RunnerName(r)}가 입었어요", $"Worn by {RunnerName(r)}")
                : Loc.T($"{RunnerName(r)}에 입히기", $"Put on {RunnerName(r)}")) { IsEnabled = !worn };
            button.Click += (_, _) =>
            {
                WearSkin(id, _pulled);
                RefreshWearButtons();
            };
            _wearButtons.Children.Add(button);
        }
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
        page.Children.Add(SectionHeader(Loc.T("러너 모음", "Runners"), Loc.T("생선을 모아 새 친구를 데려오면 고양이 대신 달리게 할 수 있어요", "Bring home new friends with fish to run in place of the cat")));
        _runnerTiles.Margin = new Thickness(0, 4, 0, 0);
        _runnerTiles.MaxWidth = 680;
        _runnerTiles.HorizontalAlignment = HorizontalAlignment.Left;
        page.Children.Add(_runnerTiles);
        page.Children.Add(HintBox(_closetHint));
        return page;
    }

    private string DefaultClosetHint() =>
        Selected is { } r ? Loc.T($"고양이 탭에서 고른 {RunnerName(r)}에게 입혀요.", $"Applies to the {RunnerName(r)} picked in the Cats tab.") : "";

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
        var soon = RunnerTile(Loc.T("새 친구", "New friend"), (ctx, box, _) => DrawMystery(ctx, box));
        soon.Price = next;
        soon.State = TileState.Unavailable;
        soon.Cursor = Cursor.Default;
        soon.PointerEntered += (_, _) =>
            _closetHint.Text = Loc.T($"새 러너를 준비하고 있어요. 생선 {GameArt.FormatCount(next)}개를 모아 두면 나오자마자 데려올 수 있어요",
                $"A new runner is on the way. Save up {GameArt.FormatCount(next)} fish to bring it home as soon as it's out");
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
            _closetHint.Text = Loc.T($"{WithTopic(sprite.DisplayName)} 아직 연습 중이라 데려올 수 없어요. 조금만 기다려 주세요!",
                $"The {Lower(sprite.DisplayName)} is still practicing, so it can't come home yet. Hang tight!");
        return tile;
    }

    private static string UpcomingTeaser(IRunnerSprite sprite, int price)
    {
        string practice = sprite.Id switch
        {
            "dachshund" => Loc.T("닥스훈트가 짧은 다리로 열심히 달리기 연습 중이에요", "The dachshund is practicing hard on its short legs"),
            "rabbit" => Loc.T("토끼가 깡충 뛰기 연습 중이에요", "The rabbit is practicing its hops"),
            _ => Loc.T($"{sprite.DisplayName}가 달리기 연습 중이에요", $"The {Lower(sprite.DisplayName)} is practicing its run"),
        };
        return Loc.T($"{practice}. 곧 출시돼요! 생선 {GameArt.FormatCount(price)}개를 모아 두면 나오자마자 데려올 수 있어요",
            $"{practice}. Coming soon! Save up {GameArt.FormatCount(price)} fish to bring it home on day one");
    }

    /// <summary>영어 문장 가운데 들어가는 러너 이름은 소문자로 쓴다("the dachshund").</summary>
    private static string Lower(string name) => name.ToLowerInvariant();

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
                ? Loc.T($"{sprite.DisplayName} — {(Selected is { } r ? RunnerName(r) : "")}로 달리고 있어요",
                        $"{sprite.DisplayName} — running as the {(Selected is { } r1 ? RunnerName(r1) : "")}")
                : Loc.T($"{sprite.DisplayName} — 눌러서 {(Selected is { } r2 ? MetricName(r2.Metric) : "")} 러너로 바꿔요",
                        $"{sprite.DisplayName} — click to make it the {(Selected is { } r3 ? MetricName(r3.Metric) : "")} runner");
        }
        return LockedHint(sprite.DisplayName, price, Loc.T("눌러서 데려와요", "Click to bring it home"));
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
            string question = Loc.T($"{sprite.DisplayName}를 생선 {GameArt.FormatCount(price)}개로 데려올까요?",
                $"Bring home the {Lower(sprite.DisplayName)} for {GameArt.FormatCount(price)} fish?");
            if (!await ConfirmPurchaseAsync(question, price)) return;
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
        Place(StatHeader(Loc.T("오늘", "Today")), 0, 2);
        Place(StatHeader(Loc.T("지금까지", "All time")), 0, 3);

        int row = 1;
        foreach (var r in _working.Runners)
        {
            Place(new RunnerIcon(r) { Width = 48, Height = 34, HorizontalAlignment = HorizontalAlignment.Left }, row, 0);
            Place(StatCell(RunnerName(r) + (r.Enabled ? "" : Loc.T(" (숨김)", " (hidden)")), TextAlignment.Left), row, 1);
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
        _todayHeadline.Text = today > 0
            ? Loc.T($"오늘 {DistanceText.Format(today)} 달렸어요", $"Ran {DistanceText.Format(today)} today")
            : Loc.T("오늘은 아직 쉬는 중이에요", "Resting so far today");
        _todayCompare.Text = today > 0 ? DistanceText.Compare(today) : Loc.T("고양이가 달리기 시작하면 여기 쌓여요", "Distance adds up here once the cats start running");

        foreach (var (id, labels) in _runnerStatLabels)
        {
            labels.Today.Text = DistanceText.Format(_stats.TodayMillimeters(id));
            labels.Total.Text = DistanceText.Format(_stats.Total(id).Millimeters);
        }

        double total = _stats.TotalMillimeters();
        _totalLine.Text = Loc.T($"지금까지 총 {DistanceText.Format(total)}", $"{DistanceText.Format(total)} in total");
        _totalCompare.Text = DistanceText.Compare(total);
        string earned = GameArt.FormatCount(_rewards.EarnedFish), spentFish = GameArt.FormatCount(_rewards.Current.SpentFish);
        _fishLine.Text = Loc.T($"지금까지 생선 {earned}개를 모았고, {spentFish}개를 썼어요", $"Earned {earned} fish so far and spent {spentFish}");

        if (ScreenCatalog.Resolve(Screens, _working.MonitorDeviceName) is not { } screen)
        {
            _pitchNote.Text = "";
            return;
        }
        var pitch = MonitorPitch.For(screen);
        _pitchNote.Text = pitch.Measured
            ? Loc.T($"{pitch.MonitorName ?? "모니터"} 실제 크기({pitch.WidthMm} × {pitch.HeightMm} mm)로 계산했어요 · 1px = {pitch.MillimetersPerPixel:0.000} mm",
                    $"Measured from {pitch.MonitorName ?? "the monitor"}'s real size ({pitch.WidthMm} × {pitch.HeightMm} mm) · 1px = {pitch.MillimetersPerPixel:0.000} mm")
            : Loc.T($"모니터 크기를 알 수 없어 화면 배율로 추정했어요 · 1px ≈ {pitch.MillimetersPerPixel:0.000} mm",
                    $"Monitor size unknown, estimated from display scaling · 1px ≈ {pitch.MillimetersPerPixel:0.000} mm");
    }

    // ───────────────────────── 일반 탭 ─────────────────────────

    private Control BuildGeneralPage()
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"),
        };

        var label = Label(Loc.T("달릴 모니터", "Monitor"), GameArt.FontBody, GameArt.Text, FontWeight.Bold);
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
        var refresh = new GameButton(Loc.T("다시 찾기", "Refresh")) { Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        refresh.Click += (_, _) => LoadMonitors();
        var monitorRow = new StackPanel { Orientation = Orientation.Horizontal, Children = { _monitor, refresh } };
        Grid.SetColumn(monitorRow, 1);
        grid.Children.Add(monitorRow);

        // 전체 화면 앱을 알아챌 수 없는 OS가 생기면 옵션을 숨긴다(지금은 Windows·macOS 모두 알아챈다).
        _hideFullScreen.Content = Loc.T("전체 화면 앱(게임·발표) 실행 중에는 숨기기", "Hide while a full-screen app (game, presentation) is running");
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

        // Windows 트레이 아이콘은 작은 정사각형이라 숫자를 붙일 자리가 없어 macOS에서만 보인다.
        _menuBarFish.Content = Loc.T("메뉴 막대 아이콘 옆에 생선 수 보여 주기", "Show fish count next to the menu bar icon");
        _menuBarFish.Margin = new Thickness(0, 10, 0, 0);
        _menuBarFish.IsVisible = OperatingSystem.IsMacOS();
        _menuBarFish.IsCheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _working.ShowFishInMenuBar = _menuBarFish.IsChecked == true;
            Commit();
        };
        Grid.SetRow(_menuBarFish, 2);
        Grid.SetColumn(_menuBarFish, 1);
        grid.Children.Add(_menuBarFish);

        // 언어를 바꾸면 메뉴 글자가 바뀌고, 설정 창은 같은 자리에서 새 언어로 다시 열린다(TrayApp).
        var languageLabel = Label(Loc.T("언어", "Language"), GameArt.FontBody, GameArt.Text, FontWeight.Bold);
        languageLabel.VerticalAlignment = VerticalAlignment.Center;
        languageLabel.Margin = new Thickness(0, 12, 16, 0);
        Grid.SetRow(languageLabel, 3);
        grid.Children.Add(languageLabel);

        _language.ItemsSource = Languages.Select(l => l.Label).ToList();
        _language.Margin = new Thickness(0, 12, 0, 0);
        _language.SelectionChanged += (_, _) =>
        {
            if (_loading || _language.SelectedIndex < 0 || _language.SelectedIndex >= Languages.Length) return;
            _working.Language = Loc.ToId(Languages[_language.SelectedIndex].Language);
            Commit();
        };
        Grid.SetRow(_language, 3);
        Grid.SetColumn(_language, 1);
        grid.Children.Add(_language);

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
            _menuBarFish.IsChecked = _working.ShowFishInMenuBar;
            _language.SelectedIndex = Array.FindIndex(Languages, l => l.Language == Loc.Parse(_working.Language));
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
        _header.SetWallet(Math.Max(0, fish - _heldRefund), _rewards.NextFishProgress, _rewards.SecondsPerFish);
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
        MetricKind.Memory => Loc.T("메모리", "Memory"),
        _ => m.ToString(),
    };
}
