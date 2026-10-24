using System.Drawing;
using System.Windows.Forms;

namespace SwallowIt;

internal sealed class TrayApp : ApplicationContext
{
    private const string AppTitle = SwallowIt.MainForm.WindowTitle;

    private readonly EntryStore _store = new();
    private readonly List<ServiceEntry> _entries;
    private readonly MainForm _mainForm;
    private readonly NotifyIcon _trayIcon;
    private readonly Icon _appIcon;
    private readonly System.Windows.Forms.Timer _stateTimer;

    private ToolStripMenuItem _autoStartTrayItem = null!;
    private ToolStripMenuItem _showAllTrayItem = null!;
    private ToolStripMenuItem _hideAllTrayItem = null!;

    private bool _allowExit;
    private bool _pickInProgress;
    private bool _trayTipShown;
    private string? _startupNotice;
    private bool _startupNoticeIsWarning;

    public TrayApp(bool startHidden)
    {
        var loadedEntries = _store.Load(out var loadWarning);
        _entries = loadedEntries;

        if (!string.IsNullOrWhiteSpace(loadWarning))
        {
            AppendStartupNotice(loadWarning, isWarning: true);
        }

        PrepareEntries();

        _appIcon = AppIcon.Create();
        _mainForm = new MainForm(_entries)
        {
            Icon = _appIcon
        };
        _ = _mainForm.Handle;

        WireMainFormEvents();

        _trayIcon = new NotifyIcon
        {
            Icon = _appIcon,
            Text = "SwallowIt — 本地窗口收纳",
            Visible = true,
            ContextMenuStrip = BuildTrayMenu()
        };
        _trayIcon.MouseClick += OnTrayMouseClick;
        _trayIcon.DoubleClick += (_, _) => ShowPanel();

        _stateTimer = new System.Windows.Forms.Timer
        {
            Interval = 1500
        };
        _stateTimer.Tick += (_, _) => RefreshStates();
        _stateTimer.Start();

        if (!string.IsNullOrWhiteSpace(_startupNotice))
        {
            _mainForm.SetNotice(_startupNotice, _startupNoticeIsWarning);
        }

        if (startHidden)
        {
            ShowBalloon("SwallowIt 已在托盘中运行。", ToolTipIcon.Info);
        }
        else
        {
            ShowPanel();
        }

        if (!string.IsNullOrWhiteSpace(_startupNotice))
        {
            ShowBalloon(_startupNotice, _startupNoticeIsWarning ? ToolTipIcon.Warning : ToolTipIcon.Info);
        }
    }

    private void WireMainFormEvents()
    {
        _mainForm.CollectRequested += BeginCollect;
        _mainForm.ShowAllRequested += ShowAll;
        _mainForm.HideAllRequested += HideAll;
        _mainForm.ExitRequested += ExitApplication;
        _mainForm.HiddenToTray += OnHiddenToTray;
        _mainForm.ShowEntryRequested += ShowEntry;
        _mainForm.HideEntryRequested += HideEntry;
        _mainForm.RemoveEntryRequested += RemoveEntry;
        _mainForm.RemoveGoneRequested += RemoveGone;
        _mainForm.RenameCommitted += OnRenameCommitted;
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var openPanelItem = new ToolStripMenuItem("打开面板");
        openPanelItem.Font = new Font(openPanelItem.Font, FontStyle.Bold);
        openPanelItem.Click += (_, _) => ShowPanel();

        var collectItem = new ToolStripMenuItem("收纳窗口...");
        collectItem.Click += (_, _) => BeginCollect();

        _showAllTrayItem = new ToolStripMenuItem("全部显示");
        _showAllTrayItem.Click += (_, _) => ShowAll();

        _hideAllTrayItem = new ToolStripMenuItem("全部收纳");
        _hideAllTrayItem.Click += (_, _) => HideAll();

        _autoStartTrayItem = new ToolStripMenuItem("开机自启")
        {
            CheckOnClick = true
        };
        _autoStartTrayItem.Click += OnAutoStartTrayClicked;

        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => ExitApplication();

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(
        [
            openPanelItem,
            collectItem,
            new ToolStripSeparator(),
            _showAllTrayItem,
            _hideAllTrayItem,
            new ToolStripSeparator(),
            _autoStartTrayItem,
            new ToolStripSeparator(),
            exitItem
        ]);

        menu.Opening += (_, _) =>
        {
            _autoStartTrayItem.Checked = AutoStart.IsEnabled();
            _showAllTrayItem.Enabled = _entries.Any(entry => entry.State == EntryState.Collected);
            _hideAllTrayItem.Enabled = _entries.Any(entry => entry.State == EntryState.Shown);
        };

        return menu;
    }

    private void PrepareEntries()
    {
        var dirty = false;
        var restored = 0;
        var restoreFailures = 0;
        var gone = 0;

        foreach (var entry in _entries)
        {
            entry.CurrentTitle = entry.OriginalTitle;
            entry.Icon = SystemIcons.Application.ToBitmap();

            if (!WindowTools.MatchesStoredIdentity(entry))
            {
                if (entry.HiddenByUs)
                {
                    entry.HiddenByUs = false;
                    dirty = true;
                }

                entry.State = EntryState.Gone;
                gone++;
                continue;
            }

            entry.CurrentTitle = WindowTools.GetTitle(entry.Handle);
            entry.Icon.Dispose();
            entry.Icon = WindowTools.GetWindowIcon(entry.Handle, entry.ProcessId);

            if (!entry.HiddenByUs)
            {
                entry.State = EntryState.Shown;
                continue;
            }

            if (WindowTools.Show(entry.Handle, activate: false))
            {
                entry.HiddenByUs = false;
                entry.State = EntryState.Shown;
                restored++;
                dirty = true;
            }
            else
            {
                entry.State = EntryState.Collected;
                restoreFailures++;
            }
        }

        if (dirty)
        {
            try
            {
                _store.Save(_entries);
            }
            catch (Exception exception)
            {
                AppLog.Write("保存恢复结果失败", exception);
                AppendStartupNotice("保存恢复结果失败。", isWarning: true);
            }
        }

        if (restored > 0)
        {
            AppendStartupNotice($"已恢复上次异常退出时遗留的 {restored} 个隐藏窗口。", isWarning: false);
        }

        if (restoreFailures > 0)
        {
            AppendStartupNotice(
                $"有 {restoreFailures} 个隐藏窗口未能恢复（可能因为权限不足），请尝试以管理员身份运行 SwallowIt。",
                isWarning: true);
        }

        if (gone > 0)
        {
            AppendStartupNotice($"有 {gone} 条记录对应的窗口已不存在。", isWarning: true);
        }
    }

    private void AppendStartupNotice(string text, bool isWarning)
    {
        _startupNoticeIsWarning |= isWarning;
        _startupNotice = string.IsNullOrWhiteSpace(_startupNotice)
            ? text
            : $"{_startupNotice} · {text}";
    }

    private void ShowPanel()
    {
        if (!_mainForm.Visible)
        {
            _mainForm.Show();
        }

        if (_mainForm.WindowState == FormWindowState.Minimized)
        {
            _mainForm.WindowState = FormWindowState.Normal;
        }

        _mainForm.Activate();
        NativeMethods.SetForegroundWindow(_mainForm.Handle);
    }

    private void OnTrayMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        if (_mainForm.Visible)
        {
            _mainForm.Hide();
        }
        else
        {
            ShowPanel();
        }
    }

    private void OnHiddenToTray()
    {
        if (_trayTipShown)
        {
            return;
        }

        _trayTipShown = true;
        ShowBalloon("SwallowIt 仍在系统托盘中运行。点击托盘图标可以重新打开面板。", ToolTipIcon.Info);
    }

    private void BeginCollect()
    {
        if (_pickInProgress)
        {
            return;
        }

        _pickInProgress = true;
        var wasVisible = _mainForm.Visible;

        try
        {
            if (wasVisible)
            {
                _mainForm.Hide();
            }

            using var picker = new WindowPickerForm();
            var result = picker.ShowDialog();
            var selectedWindow = picker.SelectedWindow;

            if (result == DialogResult.OK && selectedWindow != IntPtr.Zero)
            {
                ShowPanel();
                AddTarget(selectedWindow);
            }
            else if (wasVisible)
            {
                ShowPanel();
            }
        }
        catch (Exception exception)
        {
            if (wasVisible)
            {
                ShowPanel();
            }

            AppLog.Write("打开窗口选择器失败", exception);
            ShowWarning("打开窗口选择器失败。\n\n详细信息已记录到日志文件。");
        }
        finally
        {
            _pickInProgress = false;
        }
    }

    private void AddTarget(IntPtr window)
    {
        var rootWindow = WindowTools.GetRootWindow(window);

        if (!WindowTools.TryValidatePickTarget(rootWindow, Environment.ProcessId, out var error))
        {
            ShowWarning(error);
            return;
        }

        var existing = _entries.FirstOrDefault(entry => entry.WindowHandle == rootWindow.ToInt64());
        if (existing is not null)
        {
            if (existing.State != EntryState.Gone)
            {
                ShowInfo("这个窗口已经在列表里了。");
                return;
            }

            _entries.Remove(existing);
            _mainForm.RemoveEntryRow(existing);
            existing.Icon?.Dispose();
        }

        var processId = WindowTools.GetProcessId(rootWindow);
        var title = WindowTools.GetTitle(rootWindow);
        var processName = WindowTools.GetProcessName(processId);

        var entry = new ServiceEntry
        {
            Name = CreateUniqueName(title, processName),
            WindowHandle = rootWindow.ToInt64(),
            ProcessId = processId,
            ProcessStartTimeTicks = WindowTools.GetProcessStartTimeTicks(processId),
            ProcessName = processName,
            WindowClass = WindowTools.GetClassName(rootWindow),
            OriginalTitle = title,
            CurrentTitle = title,
            HiddenByUs = true,
            AddedAt = DateTime.Now,
            Icon = WindowTools.GetWindowIcon(rootWindow, processId)
        };

        _entries.Add(entry);

        if (!TrySave($"新增「{entry.Name}」"))
        {
            _entries.Remove(entry);
            entry.Icon?.Dispose();
            return;
        }

        if (!WindowTools.Hide(rootWindow))
        {
            _entries.Remove(entry);
            TrySave("回滚新增条目");
            entry.Icon?.Dispose();
            ShowWarning(
                "没能隐藏这个窗口。\n\n" +
                "可能原因：目标窗口以管理员身份运行，而 SwallowIt 没有管理员权限。\n" +
                "请尝试以管理员身份启动 SwallowIt 后重试。");
            return;
        }

        entry.State = EntryState.Collected;
        _mainForm.AddEntryRow(entry);
        _mainForm.FocusEntry(entry, beginRename: true);
        ShowBalloon($"已收纳「{entry.Name}」", ToolTipIcon.Info);
    }

    private string CreateUniqueName(string title, string processName)
    {
        var baseName = string.IsNullOrWhiteSpace(title) ? processName : title.Trim();
        baseName = baseName.ReplaceLineEndings(" ").Trim();

        if (baseName.Length > 48)
        {
            baseName = baseName[..48] + "…";
        }

        if (baseName.Length == 0)
        {
            baseName = "未命名窗口";
        }

        var candidate = baseName;
        var suffix = 2;

        while (_entries.Any(entry => string.Equals(entry.Name, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{baseName} ({suffix})";
            suffix++;
        }

        return candidate;
    }

    private void ShowEntry(ServiceEntry entry)
    {
        if (!WindowTools.MatchesStoredIdentity(entry))
        {
            MarkGone(entry, "这个窗口已经不存在了。");
            return;
        }

        if (entry.State == EntryState.Collected)
        {
            if (!WindowTools.Show(entry.Handle))
            {
                ShowWarning(
                    "没能显示这个窗口。\n\n" +
                    "可能原因：目标窗口以管理员身份运行，而 SwallowIt 没有管理员权限。\n" +
                    "请尝试以管理员身份启动 SwallowIt 后重试。");
                RefreshStates();
                return;
            }

            entry.HiddenByUs = false;
            entry.State = EntryState.Shown;
            TrySave($"显示「{entry.Name}」");
        }
        else
        {
            WindowTools.BringToFront(entry.Handle);
        }

        entry.CurrentTitle = WindowTools.GetTitle(entry.Handle);
        _mainForm.UpdateEntryRow(entry);
    }

    private void HideEntry(ServiceEntry entry)
    {
        if (!WindowTools.MatchesStoredIdentity(entry))
        {
            MarkGone(entry, "这个窗口已经不存在了。");
            return;
        }

        if (entry.State == EntryState.Collected)
        {
            return;
        }

        if (entry.State == EntryState.Gone)
        {
            ShowInfo("窗口已消失，无法收纳。");
            return;
        }

        var previousHidden = entry.HiddenByUs;
        entry.HiddenByUs = true;

        if (!TrySave($"收纳「{entry.Name}」"))
        {
            entry.HiddenByUs = previousHidden;
            return;
        }

        if (!WindowTools.Hide(entry.Handle))
        {
            entry.HiddenByUs = previousHidden;
            TrySave("回滚收纳状态");

            if (!NativeMethods.IsWindow(entry.Handle))
            {
                MarkGone(entry, "这个窗口已经不存在了。");
                return;
            }

            ShowWarning(
                "没能隐藏这个窗口。\n\n" +
                "可能原因：目标窗口以管理员身份运行，而 SwallowIt 没有管理员权限。\n" +
                "请尝试以管理员身份启动 SwallowIt 后重试。");
            _mainForm.UpdateEntryRow(entry);
            return;
        }

        entry.State = EntryState.Collected;
        entry.CurrentTitle = WindowTools.GetTitle(entry.Handle);
        _mainForm.UpdateEntryRow(entry);
    }

    private void RemoveEntry(ServiceEntry entry)
    {
        var isHidden = entry.State == EntryState.Collected;
        var prompt = isHidden
            ? $"从列表中移除「{entry.Name}」？\n\n这个窗口当前处于收纳状态，移除前会先恢复显示。窗口本身不会被关闭。"
            : $"从列表中移除「{entry.Name}」？\n\n只会移除记录，不会关闭窗口。";

        var owner = _mainForm.Visible ? _mainForm : null;
        var confirmed = AppDialog.Confirm(
            owner, prompt, AppTitle, acceptText: "移除", cancelText: "取消");

        if (!confirmed)
        {
            return;
        }

        if (isHidden)
        {
            if (!WindowTools.MatchesStoredIdentity(entry) || !WindowTools.Show(entry.Handle))
            {
                ShowWarning(
                    "无法先恢复这个窗口，因此没有移除条目。\n\n" +
                    "请检查窗口是否仍然存在；如果是因为权限不足，请以管理员身份运行 SwallowIt 后再试。");
                RefreshStates();
                return;
            }

            entry.HiddenByUs = false;
            entry.State = EntryState.Shown;
        }

        _entries.Remove(entry);

        if (!TrySave($"移除「{entry.Name}」"))
        {
            _entries.Add(entry);
            _mainForm.UpdateEntryRow(entry);
            return;
        }

        _mainForm.RemoveEntryRow(entry);
        entry.Icon?.Dispose();
    }

    private void RemoveGone()
    {
        var targets = _entries.Where(entry => entry.State == EntryState.Gone).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        foreach (var entry in targets)
        {
            _entries.Remove(entry);
        }

        if (!TrySave("移除已消失记录"))
        {
            _entries.AddRange(targets);
            return;
        }

        foreach (var entry in targets)
        {
            _mainForm.RemoveEntryRow(entry);
            entry.Icon?.Dispose();
        }

        ShowBalloon($"已移除 {targets.Count} 条已消失记录。", ToolTipIcon.Info);
    }

    private void ShowAll()
    {
        var targets = _entries.Where(entry => entry.State == EntryState.Collected).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        var restored = 0;
        var failed = 0;

        foreach (var entry in targets)
        {
            if (!WindowTools.MatchesStoredIdentity(entry))
            {
                MarkGone(entry, null);
                continue;
            }

            if (!WindowTools.Show(entry.Handle, activate: false))
            {
                failed++;
                continue;
            }

            entry.HiddenByUs = false;
            entry.State = EntryState.Shown;
            entry.CurrentTitle = WindowTools.GetTitle(entry.Handle);
            _mainForm.UpdateEntryRow(entry);
            restored++;
        }

        TrySave("全部显示");

        if (failed > 0)
        {
            ShowWarning($"已恢复 {restored} 个窗口，还有 {failed} 个无法恢复（可能因为权限不足）。");
        }
        else if (restored > 0)
        {
            ShowBalloon($"已恢复 {restored} 个窗口。", ToolTipIcon.Info);
        }
    }

    private void HideAll()
    {
        var targets = _entries.Where(entry => entry.State == EntryState.Shown).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        var pending = new List<ServiceEntry>();
        foreach (var entry in targets)
        {
            if (!WindowTools.MatchesStoredIdentity(entry))
            {
                MarkGone(entry, null);
                continue;
            }

            entry.HiddenByUs = true;
            pending.Add(entry);
        }

        if (pending.Count == 0)
        {
            return;
        }

        if (!TrySave("全部收纳"))
        {
            foreach (var entry in pending)
            {
                entry.HiddenByUs = false;
            }

            return;
        }

        var hidden = 0;
        var failed = 0;

        foreach (var entry in pending)
        {
            if (!WindowTools.Hide(entry.Handle))
            {
                entry.HiddenByUs = false;
                failed++;
                _mainForm.UpdateEntryRow(entry);
                continue;
            }

            entry.State = EntryState.Collected;
            entry.CurrentTitle = WindowTools.GetTitle(entry.Handle);
            _mainForm.UpdateEntryRow(entry);
            hidden++;
        }

        TrySave("保存全部收纳结果");

        if (failed > 0)
        {
            ShowWarning($"已收纳 {hidden} 个窗口，还有 {failed} 个无法收纳（可能因为权限不足）。");
        }
        else if (hidden > 0)
        {
            ShowBalloon($"已收纳 {hidden} 个窗口。", ToolTipIcon.Info);
        }
    }

    private void RefreshStates()
    {
        var dirty = false;

        foreach (var entry in _entries)
        {
            if (!NativeMethods.IsWindow(entry.Handle)
                || WindowTools.GetProcessId(entry.Handle) != entry.ProcessId)
            {
                if (entry.State != EntryState.Gone)
                {
                    entry.State = EntryState.Gone;
                    if (entry.HiddenByUs)
                    {
                        entry.HiddenByUs = false;
                        dirty = true;
                    }

                    _mainForm.UpdateEntryRow(entry);
                }

                continue;
            }

            var visible = NativeMethods.IsWindowVisible(entry.Handle);
            var title = WindowTools.GetTitle(entry.Handle);
            var changed = false;

            if (entry.HiddenByUs && visible)
            {
                entry.HiddenByUs = false;
                entry.State = EntryState.Shown;
                dirty = true;
                changed = true;
            }
            else if (entry.HiddenByUs)
            {
                if (entry.State != EntryState.Collected)
                {
                    entry.State = EntryState.Collected;
                    changed = true;
                }
            }
            else if (entry.State != EntryState.Shown)
            {
                entry.State = EntryState.Shown;
                changed = true;
            }

            if (title != entry.CurrentTitle)
            {
                entry.CurrentTitle = title;
                changed = true;
            }

            if (changed)
            {
                _mainForm.UpdateEntryRow(entry);
            }
        }

        if (dirty)
        {
            try
            {
                _store.Save(_entries);
            }
            catch (Exception exception)
            {
                AppLog.Write("保存状态失败", exception);
                _mainForm.SetNotice("保存状态失败。", isWarning: true);
            }
        }
    }

    private void MarkGone(ServiceEntry entry, string? message)
    {
        var dirty = entry.HiddenByUs;
        entry.State = EntryState.Gone;
        entry.HiddenByUs = false;

        if (dirty)
        {
            TrySave("更新窗口状态");
        }

        _mainForm.UpdateEntryRow(entry);

        if (!string.IsNullOrEmpty(message))
        {
            ShowWarning(message);
        }
    }

    private void OnRenameCommitted(ServiceEntry entry)
    {
        TrySave($"重命名「{entry.Name}」");
    }

    private void OnAutoStartTrayClicked(object? sender, EventArgs e)
    {
        var enabled = _autoStartTrayItem.Checked;

        try
        {
            AutoStart.SetEnabled(enabled);
        }
        catch (Exception exception)
        {
            _autoStartTrayItem.Checked = !enabled;
            AppLog.Write("设置开机自启失败", exception);
            ShowWarning("设置开机自启失败。\n\n详细信息已记录到日志文件。");
        }
    }

    private bool TrySave(string action)
    {
        try
        {
            _store.Save(_entries);
            return true;
        }
        catch (Exception exception)
        {
            AppLog.Write($"保存本地记录失败（{action}）", exception);
            ShowMessage(
                $"保存本地记录失败（{action}）。\n\n详细信息已记录到日志文件。",
                MessageBoxIcon.Error);
            return false;
        }
    }

    private void ExitApplication()
    {
        if (_allowExit)
        {
            return;
        }

        _allowExit = true;
        _stateTimer.Stop();

        var failures = new List<ServiceEntry>();
        var hiddenEntries = _entries.Where(entry => entry.HiddenByUs).ToList();

        foreach (var entry in hiddenEntries)
        {
            if (!NativeMethods.IsWindow(entry.Handle))
            {
                entry.HiddenByUs = false;
                continue;
            }

            if (!WindowTools.Show(entry.Handle, activate: false))
            {
                failures.Add(entry);
                continue;
            }

            entry.HiddenByUs = false;
            entry.State = EntryState.Shown;
        }

        try
        {
            _store.Save(_entries);
        }
        catch (Exception exception)
        {
            AppLog.Write("退出前保存状态失败", exception);
            ShowMessage("退出前保存状态失败。\n\n详细信息已记录到日志文件。", MessageBoxIcon.Warning);
        }

        if (failures.Count > 0)
        {
            var names = string.Join("、", failures.Take(5).Select(entry => $"「{entry.Name}」"));
            var owner = _mainForm.Visible ? _mainForm : null;
            var confirmed = AppDialog.Confirm(
                owner,
                $"有 {failures.Count} 个窗口无法恢复显示：{names}\n\n" +
                "这些窗口可能仍处于隐藏状态。通常是因为目标窗口以管理员身份运行；重新启动 SwallowIt（最好以管理员身份）后可以再次尝试恢复。\n\n" +
                "仍要退出吗？",
                AppTitle,
                acceptText: "仍要退出",
                cancelText: "取消");

            if (!confirmed)
            {
                _allowExit = false;
                _stateTimer.Start();
                RefreshStates();
                return;
            }
        }

        _stateTimer.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _mainForm.PrepareForExit();
        _mainForm.Close();
        _mainForm.Dispose();
        _appIcon.Dispose();
        ExitThread();
    }

    private void ShowBalloon(string text, ToolTipIcon icon)
    {
        if (!_trayIcon.Visible)
        {
            return;
        }

        _trayIcon.BalloonTipTitle = "SwallowIt";
        _trayIcon.BalloonTipText = text;
        _trayIcon.BalloonTipIcon = icon;
        _trayIcon.ShowBalloonTip(4000);
    }

    private void ShowWarning(string text)
    {
        ShowMessage(text, MessageBoxIcon.Warning);
    }

    private void ShowInfo(string text)
    {
        ShowMessage(text, MessageBoxIcon.Information);
    }

    private void ShowMessage(string text, MessageBoxIcon icon)
    {
        var owner = _mainForm.Visible ? _mainForm : null;

        switch (icon)
        {
            case MessageBoxIcon.Error:
                AppDialog.ShowError(owner, text, AppTitle);
                break;
            case MessageBoxIcon.Warning:
                AppDialog.ShowWarning(owner, text, AppTitle);
                break;
            default:
                AppDialog.ShowInfo(owner, text, AppTitle);
                break;
        }
    }
}
