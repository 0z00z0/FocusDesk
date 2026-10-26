using CommunityToolkit.WinUI.Controls;
using FocusDesk.Helpers;
using FocusDesk.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace FocusDesk.UI;

/// <summary>The Program focus page: the named sessions, built from ChargeKeeper's Smart Charge Presets
/// section (commit 5727ea5). Every control greys while a session runs, and every write checks again;
/// only the running session's program rows open during its grace period.</summary>
public sealed partial class ProgramFocusSettingsPanel : UserControl
{
    private readonly PresetRows.Section _list;
    // A control with a key opens for that named session's grace period.
    private readonly List<(Control Control, Func<bool> WhenUnlocked, string? GraceKey)> _lockables = [];
    private readonly Dictionary<string, (SettingsExpander Row, TextBox Name, TextBlock Error)> _rows = [];
    private bool _watching;
    private bool _saving;

    public ProgramFocusSettingsPanel()
    {
        InitializeComponent();
        PageTitle.Text = AppText.Get("FocusProgramFocusHeading");
        NamedSessionsHeader.Heading = AppText.Get("NamedSessionsHeading");
        NamedSessionsDescriptionText.Text = AppText.Get("NamedSessionsDescription");
        AddNamedSessionButton.Content = AppText.Get("NamedSessionAdd");
        SaveOpenProgramsButton.Content = AppText.Get("NamedSessionSaveOpen");

        _list = new PresetRows.Section
        {
            Panel     = NamedSessionsListPanel,
            Count     = () => SettingsService.Read(s => s.NamedSessions.Count),
            BuildRow  = i => BuildRow(SettingsService.Read(s => s.NamedSessions[i].Key)),
            ActiveTag = ActiveKey,
            // Hidden while a session runs: the copied marker paints a disabled button as "In use".
            OffersActivation = _ => !FocusSessionService.LeversAreLocked,
            ActiveTip = AppText.Get("NamedSessionInUseInfo"),
            IdleTip   = AppText.Get("NamedSessionActivateInfo"),
        };
        Loaded += (_, _) => Reload();
        _latest = this;
    }

    /// <summary>Follows the session while the page is on screen.</summary>
    public void Watch()
    {
        Reload();
        if (_watching) return;
        FocusSessionService.Changed += OnSessionChanged;
        _watching = true;
    }

    public void Unwatch()
    {
        if (!_watching) return;
        FocusSessionService.Changed -= OnSessionChanged;
        _watching = false;
    }

    private void OnSessionChanged() => DispatcherQueue.TryEnqueue(ApplySessionLock);

    /// <summary>Builds every row again from the stored list.</summary>
    public void Reload()
    {
        _lockables.Clear();
        _rows.Clear();
        _list.Rebuild();
        ApplySessionLock();

        if (IsLoaded && _pendingNew is { } saved)
        {
            _pendingNew = null;
            ShowNew(saved.Key, saved.LeftOut);
        }
    }

    /// <summary>Greys every control while a session runs and moves the marking to the running one.</summary>
    public void ApplySessionLock()
    {
        bool locked = FocusSessionService.LeversAreLocked;
        foreach (var (control, whenUnlocked, graceKey) in _lockables)
            control.IsEnabled = (!locked || (graceKey is not null && FocusSessionService.RowsOpenInGrace(graceKey))) && whenUnlocked();

        bool full = SettingsService.Read(s => s.NamedSessions.Count) >= NamedSessions.Maximum;
        AddNamedSessionButton.IsEnabled = !locked && !full;
        SaveOpenProgramsButton.IsEnabled = !locked && !full && !_saving;
        ToolTipService.SetToolTip(AddNamedSessionFrame, full ? AppText.Get("NamedSessionAddFullInfo") : null);
        _list.RefreshActivation();
    }

    /// <summary>The running session's own named session while one runs on one, the one in use otherwise.</summary>
    private static object? ActiveKey()
    {
        var running = FocusSessionService.Current;
        return SettingsService.Read(s =>
            (running.IsRunning ? NamedSessions.Find(s, running.NamedSession) : null)?.Key ?? NamedSessions.InUse(s).Key);
    }

    private static string Summary(NamedSession session)
    {
        int starts = session.Programs.Count(p => p.StartWithSession);
        return string.Join(", ",
            session.Programs.Count == 1
                ? AppText.Get("NamedSessionSummaryOne")
                : AppText.Format("NamedSessionSummary", session.Programs.Count),
            starts == 1
                ? AppText.Get("NamedSessionSummaryStartOne")
                : AppText.Format("NamedSessionSummaryStart", starts));
    }

    /// <summary>One session's row. Every commit looks the session up by its key, so a reload cannot
    /// leave a closure editing an orphaned copy.</summary>
    private SettingsExpander BuildRow(string key)
    {
        var session = SettingsService.Read(s => NamedSessions.Find(s, key)!);

        var nameBox = new TextBox { Text = session.Name, MinWidth = 220, MaxLength = NamedSessions.NameLength };
        var addProgram = new Button { Content = AppText.Get("FocusProgramsAddButton") };

        var modeBox = new ComboBox { MinWidth = 260 };
        foreach (var mode in ProgramFocusText.Modes)
            modeBox.Items.Add(new ComboBoxItem { Content = ProgramFocusText.ModeName(mode), Tag = mode });
        modeBox.SelectedIndex = Array.IndexOf(ProgramFocusText.Modes, session.Mode);
        // Attached after the selection is set, so building the row writes nothing.
        modeBox.SelectionChanged += (_, _) =>
        {
            if (modeBox.SelectedItem is ComboBoxItem { Tag: ProgramFocusMode chosen }) CommitMode(key, chosen);
        };

        List<SettingsCard> cards =
        [
            new SettingsCard { Header = AppText.Get("NamedSessionNameHeader"), Content = nameBox },
            new SettingsCard { Header = AppText.Get("NamedSessionModeHeader"), Content = modeBox },
            new SettingsCard
            {
                Header      = AppText.Get("FocusProgramsHeader"),
                Description = AppText.Get("FocusProgramsDescription"),
                Content     = addProgram,
            },
        ];

        PresetRows.Parts? built = null;
        cards.AddRange(ProgramCards(key, verdict => ShowRefusal(built!, verdict)));
        cards.Add(PositionsCard(key));

        var row = PresetRows.Build(session.Name, Summary(session), key, cards,
                                   ProgramFocusText.CriticalBrush(), AppText.Get("NamedSessionDelete"));
        built = row;

        row.Activate.Click += (_, _) => Activate(key);
        nameBox.LostFocus += (_, _) => CommitName(key, nameBox, row);
        nameBox.KeyDown   += (_, e) => { if (e.Key == VirtualKey.Enter) CommitName(key, nameBox, row); };
        addProgram.Click  += (_, _) => PickProgram(key, row);
        row.Delete.Click  += (_, _) => Delete(key);

        bool last = SettingsService.Read(s => s.NamedSessions.Count) <= 1;
        ToolTipService.SetToolTip(row.DeleteFrame, last ? AppText.Get("NamedSessionDeleteLastInfo") : null);
        _lockables.Add((nameBox, () => true, null));
        _lockables.Add((addProgram, () => true, key));
        _lockables.Add((modeBox, () => true, null));
        _lockables.Add((row.Delete, () => !last, null));
        _rows[key] = (row.Expander, nameBox, row.Error);
        return row.Expander;
    }

    /// <summary>One card per program of the session: its icon and name, "can run", "start with the
    /// session" and Remove. A tick the session's mode does not use is disabled and keeps its value, and
    /// so is "can run" for a Windows program, each with the reason on hover.</summary>
    private IEnumerable<SettingsCard> ProgramCards(string key, Action<FocusAllowVerdict> refused)
    {
        string windows = WindowsPrograms.Folder;
        var (mode, programs) = SettingsService.Read(s =>
        {
            var session = NamedSessions.Find(s, key)!;
            return (session.Mode, session.Programs.ToList());
        });

        foreach (var entry in programs)
        {
            var program = entry;
            var offer = ProgramCatalogue.OfferFor(entry, windows);
            bool canRunUsed = mode != ProgramFocusMode.OpenMarkedAllowAll;
            bool startUsed  = mode != ProgramFocusMode.AllowListed;

            var canRun = new CheckBox
            {
                Content   = AppText.Get("FocusProgramCanRun"),
                IsChecked = offer == ProgramOffer.AlwaysUsable || entry.CanRun,
            };
            canRun.Click += (_, _) => ChangePrograms(key, refused, list => FocusAllowedPrograms.SetCanRun(
                list, program.Kind, program.Id, canRun.IsChecked == true, Locked(key)));

            var start = new CheckBox
            {
                Content   = AppText.Get("FocusProgramStartWithSession"),
                IsChecked = entry.StartWithSession,
            };
            start.Click += (_, _) => ChangePrograms(key, refused, list => FocusAllowedPrograms.SetStartWithSession(
                list, program.Kind, program.Id, start.IsChecked == true, Locked(key)));

            var remove = new Button { Content = AppText.Get("FocusProgramRemove") };
            remove.Click += (_, _) => ChangePrograms(key, refused, list => FocusAllowedPrograms.Remove(
                list, program.Kind, program.Id, Locked(key)));

            _lockables.Add((canRun, () => canRunUsed && offer == ProgramOffer.Limitable, key));
            _lockables.Add((start, () => startUsed, key));
            _lockables.Add((remove, () => true, key));

            string? runWhy = !canRunUsed ? AppText.Get("FocusProgramCanRunUnusedInfo")
                : offer == ProgramOffer.AlwaysUsable ? AppText.Get("FocusProgramWindowsRunInfo") : null;
            string? startWhy = startUsed ? null : AppText.Get("FocusProgramStartUnusedInfo");

            var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            controls.Children.Add(WithReason(canRun, runWhy));
            controls.Children.Add(WithReason(start, startWhy));
            controls.Children.Add(remove);

            var icon = new ImageIcon { Width = 24, Height = 24 };
            ProgramIconLoader.Request(entry.StartEntry ?? entry.Id, image => icon.Source = image);

            yield return new SettingsCard
            {
                HeaderIcon  = icon,
                Header      = ProgramNames.For(entry),
                Description = ProgramCatalogue.IsPresent(entry)
                    ? FocusAllowedPrograms.Describe(entry)
                    : AppText.Get("FocusProgramNotFound"),
                Content     = controls,
            };
        }
    }

    /// <summary>"Save program positions" for this session; never locked, so it is not among the lockables.</summary>
    private static SettingsCard PositionsCard(string key)
    {
        var save = new Button { Content = AppText.Get("SavePositionsButton") };
        ToolTipService.SetToolTip(save, AppText.Get("SavePositionsInfo"));
        var card = new SettingsCard { Header = AppText.Get("NamedSessionPositionsHeader"), Content = save };
        save.Click += async (_, _) =>
        {
            save.IsEnabled = false;
            try
            {
                var (outcome, saved) = await ProgramPositions.SaveAsync(key);
                card.Description = ProgramFocusText.PositionsSaved(outcome, saved);
            }
            catch (Exception ex) { AppLog.Error("ProgramFocusSettingsPanel.SavePositions", ex); }
            finally { save.IsEnabled = true; }
        };
        return card;
    }

    private static FrameworkElement WithReason(Control control, string? reason)
    {
        if (reason is null) return control;
        var frame = new Grid { Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        frame.Children.Add(control);
        ToolTipService.SetToolTip(frame, reason);
        return frame;
    }

    /// <summary>Marks the session in use. Never starts a session.</summary>
    private void Activate(string key)
    {
        if (FocusSessionService.LeversAreLocked) { ApplySessionLock(); return; }
        SettingsService.Update(s => { if (NamedSessions.Find(s, key) is not null) s.SelectedNamedSession = key; });
        _list.RefreshActivation();
    }

    /// <summary>Saves the name as typed, capped; a blank edit puts the previous name back.</summary>
    private void CommitName(string key, TextBox nameBox, PresetRows.Parts row)
    {
        string previous = SettingsService.Read(s => NamedSessions.Find(s, key)?.Name) ?? "";
        string name = NamedSessions.CleanName(nameBox.Text, previous);
        nameBox.Text = name;
        if (name == previous) return;
        Write(key, s => NamedSessions.Find(s, key)!.Name = name, row);
    }

    /// <summary>Saves the session's mode and builds its row again, so the ticks the mode does not use grey.</summary>
    private void CommitMode(string key, ProgramFocusMode mode)
    {
        if (FocusSessionService.LeversAreLocked || SettingsService.Read(s => NamedSessions.Find(s, key)) is null)
        {
            DispatcherQueue.TryEnqueue(() => ReloadKeepingOpen(key));
            return;
        }
        if (SettingsService.Read(s => NamedSessions.Find(s, key)!.Mode) == mode) return;

        SettingsService.Update(s => { if (NamedSessions.Find(s, key) is { } session) session.Mode = mode; });
        DispatcherQueue.TryEnqueue(() => ReloadKeepingOpen(key));
    }

    /// <summary>One write to one session, refused while a session runs, then the row's header and
    /// summary redrawn.</summary>
    private void Write(string key, Action<AppSettings> change, PresetRows.Parts row)
    {
        if (FocusSessionService.LeversAreLocked) { Reload(); return; }
        if (SettingsService.Read(s => NamedSessions.Find(s, key)) is null) { Reload(); return; }
        SettingsService.Update(change);

        var session = SettingsService.Read(s => NamedSessions.Find(s, key)!);
        row.Header.Text = session.Name;
        row.Expander.Description = Summary(session);
    }

    private void PickProgram(string key, PresetRows.Parts row)
    {
        try
        {
            var listed = SettingsService.Read(s => NamedSessions.Find(s, key)?.Programs.Select(p => (p.Kind, p.Id)).ToList()) ?? [];
            new ProgramPickerWindow(chosen => DispatcherQueue.TryEnqueue(() => ChangePrograms(
                key, verdict => ShowRefusal(row, verdict),
                list => FocusAllowedPrograms.Add(list, chosen.ToEntry(), Locked(key)))),
                listed, Enum.GetValues<FocusProgramKind>())
                .Activate();
        }
        catch (Exception ex) { AppLog.Error("ProgramFocusSettingsPanel.PickProgram", ex); }
    }

    /// <summary>Runs one change against a copy of the session's list and writes it back only where
    /// it moved, so a refusal leaves the document untouched and says why.</summary>
    private void ChangePrograms(string key, Action<FocusAllowVerdict> refused,
                                Func<IList<FocusProgramEntry>, FocusAllowVerdict> change)
    {
        if (SettingsService.Read(s => NamedSessions.Find(s, key)?.Programs.ToList()) is not { } list) { Reload(); return; }
        var verdict = change(list);

        if (verdict is FocusAllowVerdict.Added or FocusAllowVerdict.Removed or FocusAllowVerdict.Changed)
        {
            // The running session's rows go through the engine, which checks the grace period again.
            var outcome = FocusSessionService.IsRunning
                ? FocusSessionService.ChangeRunningPrograms(key, list, ActionCause.SettingsPage("Program focus"))
                : WriteIdle(key, list);
            if (outcome == FocusGraceChange.Changed) { ReloadKeepingOpen(key); return; }
            verdict = outcome == FocusGraceChange.LeverRefused ? FocusAllowVerdict.NothingKeptUsable : FocusAllowVerdict.SessionRunning;
        }
        refused(verdict);
        if (verdict == FocusAllowVerdict.SessionRunning) ApplySessionLock();
    }

    private static FocusGraceChange WriteIdle(string key, List<FocusProgramEntry> list)
    {
        SettingsService.Update(s => { if (NamedSessions.Find(s, key) is { } session) session.Programs = list; });
        return FocusGraceChange.Changed;
    }

    private static bool Locked(string key) =>
        FocusSessionService.LeversAreLocked && !FocusSessionService.RowsOpenInGrace(key);

    private static void ShowRefusal(PresetRows.Parts row, FocusAllowVerdict verdict)
    {
        row.Error.Text = ProgramFocusText.Refusal(verdict);
        row.Error.Visibility = row.Error.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Delete(string key)
    {
        if (FocusSessionService.LeversAreLocked) { ApplySessionLock(); return; }
        SettingsService.Update(s => NamedSessions.Delete(s, key));
        Reload();
    }

    /// <summary>Adds "New session", opened with its name box focused.</summary>
    private void OnAddNamedSession(object sender, RoutedEventArgs e)
    {
        if (FocusSessionService.LeversAreLocked) { ApplySessionLock(); return; }
        string? added = null;
        SettingsService.Update(s => added = NamedSessions.Add(s, AppText.Get("NamedSessionNewName"))?.Key);
        if (added is null) { ApplySessionLock(); return; }

        OpenWithNameFocused(added);
    }

    private async void OnSaveOpenPrograms(object sender, RoutedEventArgs e)
    {
        if (FocusSessionService.LeversAreLocked || _saving) { ApplySessionLock(); return; }
        _saving = true;
        ApplySessionLock();
        try
        {
            var (added, leftOut) = await SaveOpenProgramsAsync();
            if (added is not null) ShowNew(added, leftOut);
        }
        catch (Exception ex) { AppLog.Error("ProgramFocusSettingsPanel.SaveOpenPrograms", ex); }
        finally
        {
            _saving = false;
            ApplySessionLock();
        }
    }

    /// <summary>A session in mode 2 from the programs open now, at most a full list; also the dashboard's save.</summary>
    internal static async Task<(string? Key, int LeftOut)> SaveOpenProgramsAsync()
    {
        var open = ProgramCatalogue.OpenNow(await ProgramCatalogue.BuildAsync(), ProgramCatalogue.OpenWindows());
        if (FocusSessionService.LeversAreLocked) return (null, 0);

        string? added = null;
        SettingsService.Update(s => added = NamedSessions.AddFrom(
            s, AppText.Get("NamedSessionNewName"),
            open.Take(FocusAllowedPrograms.Maximum).Select(c => c.ToEntry()))?.Key);
        return (added, Math.Max(0, open.Count - FocusAllowedPrograms.Maximum));
    }

    // Shown once loaded: a page built for it loads after it enters, and loading rebuilds the list.
    private static (string Key, int LeftOut)? _pendingNew;
    private static ProgramFocusSettingsPanel? _latest;

    /// <summary>Opens Settings on this page with a session saved from the dashboard expanded.</summary>
    internal static void ShowSaved(string key, int leftOut)
    {
        _pendingNew = (key, leftOut);
        SettingsShellHost.Open(SettingsShellHost.ProgramFocusTag);
        // The page already on screen enters nothing again.
        if (_pendingNew is not null && _latest is { IsLoaded: true } page) page.Reload();
    }

    private void ShowNew(string key, int leftOut)
    {
        OpenWithNameFocused(key);
        if (leftOut > 0 && _rows.TryGetValue(key, out var row))
        {
            row.Error.Text = AppText.Format("NamedSessionSaveOpenLeftOut", leftOut);
            row.Error.Visibility = Visibility.Visible;
        }
    }

    private void OpenWithNameFocused(string key)
    {
        ReloadKeepingOpen(key);
        if (_rows.TryGetValue(key, out var row))
            row.Name.Loaded += (_, _) => { row.Name.Focus(FocusState.Programmatic); row.Name.SelectAll(); };
    }

    /// <summary>Rebuilds the list with one row left expanded, so the edit just made stays in view.</summary>
    private void ReloadKeepingOpen(string key)
    {
        Reload();
        if (_rows.TryGetValue(key, out var row)) row.Row.IsExpanded = true;
    }
}

/// <summary>Texts and brushes the program rows share.</summary>
internal static class ProgramFocusText
{
    /// <summary>The modes in the order the list offers them.</summary>
    public static readonly ProgramFocusMode[] Modes =
        [ProgramFocusMode.AllowListed, ProgramFocusMode.OpenMarkedAllowListed, ProgramFocusMode.OpenMarkedAllowAll];

    public static string ModeName(ProgramFocusMode mode) => mode switch
    {
        ProgramFocusMode.OpenMarkedAllowListed => AppText.Get("ProgramFocusModeOpenMarkedAllowListed"),
        ProgramFocusMode.OpenMarkedAllowAll    => AppText.Get("ProgramFocusModeOpenMarkedAllowAll"),
        _                                      => AppText.Get("ProgramFocusModeAllowListed"),
    };

    /// <summary>Why the list did not move, in the words of whoever pressed the button.</summary>
    public static string Refusal(FocusAllowVerdict verdict) => verdict switch
    {
        FocusAllowVerdict.SessionRunning => AppText.Get("FocusProgramsRefusalSessionRunning"),
        FocusAllowVerdict.NotAProgram    => AppText.Get("FocusProgramsRefusalNotAProgram"),
        FocusAllowVerdict.ListFull       => AppText.Format("FocusProgramsRefusalListFull", FocusAllowedPrograms.Maximum),
        FocusAllowVerdict.AlreadyAllowed => AppText.Get("FocusProgramsRefusalAlreadyAllowed"),
        FocusAllowVerdict.NotAllowed     => AppText.Get("FocusProgramsRefusalNotAllowed"),
        FocusAllowVerdict.NothingKeptUsable => AppText.Get("PopOutRefusalLeverRefused"),
        _                                => "",
    };

    /// <summary>What a press of "Save program positions" came to, on the dashboard and in Settings alike.</summary>
    public static string PositionsSaved(PositionsSaveOutcome outcome, int saved) => outcome switch
    {
        PositionsSaveOutcome.Saved when saved == 1 => AppText.Get("SavePositionsDoneOne"),
        PositionsSaveOutcome.Saved                 => AppText.Format("SavePositionsDone", saved),
        PositionsSaveOutcome.NoOpenWindow          => AppText.Get("SavePositionsNone"),
        PositionsSaveOutcome.SessionMissing        => AppText.Get("SavePositionsSessionMissing"),
        _                                          => AppText.Get("SavePositionsFailed"),
    };

    /// <summary>The critical brush for inline refusals, looked up without throwing.</summary>
    public static Microsoft.UI.Xaml.Media.Brush? CriticalBrush() =>
        Application.Current.Resources.TryGetValue("SystemFillColorCriticalBrush", out var brush)
            ? brush as Microsoft.UI.Xaml.Media.Brush
            : null;
}
