using CommunityToolkit.WinUI.Controls;
using FocusDesk.Helpers;
using FocusDesk.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace FocusDesk.UI;

/// <summary>The Program focus page: the named sessions, built from ChargeKeeper's Smart Charge Presets
/// section (commit 5727ea5). Every control greys while a session runs, and every write checks again.</summary>
public sealed partial class ProgramFocusSettingsPanel : UserControl
{
    private readonly PresetRows.Section _list;
    private readonly List<(Control Control, Func<bool> WhenUnlocked)> _lockables = [];
    private readonly Dictionary<string, (SettingsExpander Row, TextBox Name)> _rows = [];
    private bool _watching;

    public ProgramFocusSettingsPanel()
    {
        InitializeComponent();
        PageTitle.Text = AppText.Get("FocusProgramFocusHeading");
        NamedSessionsHeader.Heading = AppText.Get("NamedSessionsHeading");
        NamedSessionsDescriptionText.Text = AppText.Get("NamedSessionsDescription");
        AddNamedSessionButton.Content = AppText.Get("NamedSessionAdd");

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
    }

    /// <summary>Greys every control while a session runs and moves the marking to the running one.</summary>
    public void ApplySessionLock()
    {
        bool locked = FocusSessionService.LeversAreLocked;
        foreach (var (control, whenUnlocked) in _lockables) control.IsEnabled = !locked && whenUnlocked();

        bool full = SettingsService.Read(s => s.NamedSessions.Count) >= NamedSessions.Maximum;
        AddNamedSessionButton.IsEnabled = !locked && !full;
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

    private static string Summary(NamedSession session) => session.Programs.Count == 1
        ? AppText.Get("NamedSessionSummaryOne")
        : AppText.Format("NamedSessionSummary", session.Programs.Count);

    /// <summary>One session's row. Every commit looks the session up by its key, so a reload cannot
    /// leave a closure editing an orphaned copy.</summary>
    private SettingsExpander BuildRow(string key)
    {
        var session = SettingsService.Read(s => NamedSessions.Find(s, key)!);

        var nameBox = new TextBox { Text = session.Name, MinWidth = 220, MaxLength = NamedSessions.NameLength };
        var addProgram = new Button { Content = AppText.Get("FocusProgramsAddButton") };

        List<SettingsCard> cards =
        [
            new SettingsCard { Header = AppText.Get("NamedSessionNameHeader"), Content = nameBox },
            new SettingsCard
            {
                Header      = AppText.Get("FocusProgramsHeader"),
                Description = AppText.Get("FocusProgramsDescription"),
                Content     = addProgram,
            },
        ];

        PresetRows.Parts? built = null;
        cards.AddRange(ProgramCards(key, verdict => ShowRefusal(built!, verdict)));

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
        _lockables.Add((nameBox, () => true));
        _lockables.Add((addProgram, () => true));
        _lockables.Add((row.Delete, () => !last));
        _rows[key] = (row.Expander, nameBox);
        return row.Expander;
    }

    /// <summary>One card per program of the session: its icon and name, "can run" and Remove. For a
    /// Windows program "can run" is disabled, with the reason on hover.</summary>
    private IEnumerable<SettingsCard> ProgramCards(string key, Action<FocusAllowVerdict> refused)
    {
        string windows = WindowsPrograms.Folder;
        foreach (var entry in SettingsService.Read(s => NamedSessions.Find(s, key)!.Programs.ToList()))
        {
            var program = entry;
            var offer = ProgramCatalogue.OfferFor(entry, windows);

            var canRun = new CheckBox
            {
                Content   = AppText.Get("FocusProgramCanRun"),
                IsChecked = offer == ProgramOffer.AlwaysUsable || entry.CanRun,
            };
            canRun.Click += (_, _) => ChangePrograms(key, refused, list => FocusAllowedPrograms.SetCanRun(
                list, program.Kind, program.Id, canRun.IsChecked == true, FocusSessionService.LeversAreLocked));

            var remove = new Button { Content = AppText.Get("FocusProgramRemove") };
            remove.Click += (_, _) => ChangePrograms(key, refused, list => FocusAllowedPrograms.Remove(
                list, program.Kind, program.Id, FocusSessionService.LeversAreLocked));

            _lockables.Add((canRun, () => offer == ProgramOffer.Limitable));
            _lockables.Add((remove, () => true));

            string? runWhy = offer == ProgramOffer.AlwaysUsable ? AppText.Get("FocusProgramWindowsRunInfo") : null;

            var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            controls.Children.Add(WithReason(canRun, runWhy));
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
            new ProgramPickerWindow(chosen => DispatcherQueue.TryEnqueue(() => ChangePrograms(
                key, verdict => ShowRefusal(row, verdict),
                list => FocusAllowedPrograms.Add(list, chosen.ToEntry(), FocusSessionService.LeversAreLocked))))
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
            SettingsService.Update(s => { if (NamedSessions.Find(s, key) is { } session) session.Programs = list; });
            ReloadKeepingOpen(key);
            return;
        }
        refused(verdict);
        if (verdict == FocusAllowVerdict.SessionRunning) ApplySessionLock();
    }

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

        ReloadKeepingOpen(added);
        if (_rows.TryGetValue(added, out var row))
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
    /// <summary>Why the list did not move, in the words of whoever pressed the button.</summary>
    public static string Refusal(FocusAllowVerdict verdict) => verdict switch
    {
        FocusAllowVerdict.SessionRunning => AppText.Get("FocusProgramsRefusalSessionRunning"),
        FocusAllowVerdict.NotAProgram    => AppText.Get("FocusProgramsRefusalNotAProgram"),
        FocusAllowVerdict.ListFull       => AppText.Format("FocusProgramsRefusalListFull", FocusAllowedPrograms.Maximum),
        FocusAllowVerdict.AlreadyAllowed => AppText.Get("FocusProgramsRefusalAlreadyAllowed"),
        FocusAllowVerdict.NotAllowed     => AppText.Get("FocusProgramsRefusalNotAllowed"),
        _                                => "",
    };

    /// <summary>The critical brush for inline refusals, looked up without throwing.</summary>
    public static Microsoft.UI.Xaml.Media.Brush? CriticalBrush() =>
        Application.Current.Resources.TryGetValue("SystemFillColorCriticalBrush", out var brush)
            ? brush as Microsoft.UI.Xaml.Media.Brush
            : null;
}
