using System.Drawing;

namespace SwallowIt;

public sealed class MainForm : Form
{
    public const string WindowTitle = "SwallowIt";

    private const string RuntimeDownloadUrl = "https://dotnet.microsoft.com/zh-cn/download/dotnet/10.0";

    private readonly DataGridView _grid;
    private readonly Label _emptyState;
    private readonly ToolStripStatusLabel _summaryLabel;
    private readonly ToolStripStatusLabel _noticeLabel;
    private readonly ToolStripStatusLabel _hintLabel;
    private readonly ToolStripStatusLabel _authorLabel;
    private Button _collectButton = null!;
    private Button _showButton = null!;
    private Button _hideButton = null!;
    private Button _removeButton = null!;
    private Button _showAllButton = null!;
    private ToolStripMenuItem _showSelectedMenuItem = null!;
    private ToolStripMenuItem _hideSelectedMenuItem = null!;
    private ToolStripMenuItem _removeMenuItem = null!;
    private ToolStripMenuItem _showAllMenuItem = null!;
    private ToolStripMenuItem _hideAllMenuItem = null!;
    private ToolStripMenuItem _autoStartMenuItem = null!;

    private bool _allowClose;

    public MainForm(IReadOnlyList<ServiceEntry> entries)
    {
        Text = WindowTitle;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(780, 500);
        Size = new Size(940, 620);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Font = new Font("Microsoft YaHei UI", 9F);

        var menu = BuildMenu();
        MainMenuStrip = menu;

        _grid = BuildGrid();
        _emptyState = BuildEmptyState();
        var content = BuildContentPanel(_grid, _emptyState);

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Color.FromArgb(245, 247, 248)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 76F));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));

        _summaryLabel = new ToolStripStatusLabel("共 0 个")
        {
            ForeColor = Color.FromArgb(70, 80, 90)
        };
        _noticeLabel = new ToolStripStatusLabel
        {
            Spring = true,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(191, 90, 24),
            Visible = false
        };
        _hintLabel = new ToolStripStatusLabel("双击名称可重命名 · 关闭面板后仍驻留托盘")
        {
            Spring = true,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Color.FromArgb(140, 148, 156)
        };
        _authorLabel = new ToolStripStatusLabel("作者：小李同学32q")
        {
            ForeColor = Color.FromArgb(155, 162, 170),
            Margin = new Padding(12, 3, 0, 2)
        };

        var statusStrip = new StatusStrip
        {
            Dock = DockStyle.Fill,
            SizingGrip = false,
            BackColor = Color.White,
            Padding = new Padding(10, 0, 10, 0)
        };
        statusStrip.Items.AddRange([_summaryLabel, _noticeLabel, _hintLabel, _authorLabel]);

        table.Controls.Add(menu, 0, 0);
        table.Controls.Add(BuildHeader(), 0, 1);
        table.Controls.Add(BuildActionBar(), 0, 2);
        table.Controls.Add(content, 0, 3);
        table.Controls.Add(statusStrip, 0, 4);
        Controls.Add(table);

        foreach (var entry in entries)
        {
            AddEntryRowCore(entry);
        }

        UpdateSummary();
    }

    public event Action? CollectRequested;

    public event Action? ShowAllRequested;

    public event Action? HideAllRequested;

    public event Action? ExitRequested;

    public event Action? HiddenToTray;

    public event Action<ServiceEntry>? ShowEntryRequested;

    public event Action<ServiceEntry>? HideEntryRequested;

    public event Action<ServiceEntry>? RemoveEntryRequested;

    public event Action<ServiceEntry>? RenameCommitted;

    public void PrepareForExit()
    {
        _allowClose = true;
    }

    public void AddEntryRow(ServiceEntry entry)
    {
        AddEntryRowCore(entry);
        UpdateSummary();
    }

    public void UpdateEntryRow(ServiceEntry entry)
    {
        var row = FindRow(entry);
        if (row is null)
        {
            AddEntryRowCore(entry);
            UpdateSummary();
            return;
        }

        row.Cells["Status"].Value = entry.StateText;
        row.Cells["Title"].Value = entry.CurrentTitle;
        row.Cells["Process"].Value = entry.ProcessName;
        row.Cells["Icon"].Value = entry.Icon;
        ApplyStatusStyle(row);

        var nameCell = row.Cells["Name"];
        var isEditingName = _grid.IsCurrentCellInEditMode
            && _grid.CurrentCell is not null
            && _grid.CurrentCell.RowIndex == row.Index
            && _grid.CurrentCell.ColumnIndex == nameCell.ColumnIndex;

        if (!isEditingName)
        {
            nameCell.Value = entry.Name;
        }

        UpdateSummary();
    }

    public void RemoveEntryRow(ServiceEntry entry)
    {
        var row = FindRow(entry);
        if (row is null)
        {
            return;
        }

        var wasSelected = row.Selected;
        var removedIndex = row.Index;
        _grid.Rows.Remove(row);

        if (wasSelected && _grid.Rows.Count > 0)
        {
            var index = Math.Clamp(removedIndex, 0, _grid.Rows.Count - 1);
            _grid.CurrentCell = _grid.Rows[index].Cells["Name"];
            _grid.Rows[index].Selected = true;
        }

        UpdateSummary();
    }

    public void FocusEntry(ServiceEntry entry, bool beginRename)
    {
        var row = FindRow(entry);
        if (row is null)
        {
            return;
        }

        _grid.CurrentCell = row.Cells["Name"];
        _grid.Rows[row.Index].Selected = true;

        if (beginRename)
        {
            BeginInvoke(() =>
            {
                if (FindRow(entry) is { } currentRow)
                {
                    _grid.CurrentCell = currentRow.Cells["Name"];
                    _grid.BeginEdit(selectAll: true);
                }
            });
        }
    }

    public void RefreshSummary()
    {
        UpdateSummary();
    }

    public void SetNotice(string? text, bool isWarning = false)
    {
        var hasNotice = !string.IsNullOrWhiteSpace(text);
        _noticeLabel.Text = text ?? string.Empty;
        _noticeLabel.ToolTipText = text ?? string.Empty;
        _noticeLabel.ForeColor = isWarning ? Color.FromArgb(191, 90, 24) : Color.FromArgb(80, 92, 104);
        _noticeLabel.Visible = hasNotice;
        _hintLabel.Visible = !hasNotice;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            HiddenToTray?.Invoke();
            return;
        }

        base.OnFormClosing(e);
    }

    private DataGridView BuildGrid()
    {
        var grid = new SmoothDataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AllowUserToOrderColumns = false,
            RowHeadersVisible = false,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            GridColor = Color.FromArgb(235, 238, 240),
            EnableHeadersVisualStyles = false,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
            ColumnHeadersHeight = 36,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            Font = new Font("Microsoft YaHei UI", 9.5F),
            AccessibleName = "已收纳窗口列表"
        };

        grid.RowTemplate.Height = 44;

        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(247, 249, 250),
            ForeColor = Color.FromArgb(96, 106, 116),
            SelectionBackColor = Color.FromArgb(247, 249, 250),
            SelectionForeColor = Color.FromArgb(96, 106, 116),
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
            Alignment = DataGridViewContentAlignment.MiddleLeft,
            Padding = new Padding(6, 0, 6, 0),
            WrapMode = DataGridViewTriState.False
        };

        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.White,
            ForeColor = Color.FromArgb(35, 42, 50),
            SelectionBackColor = Color.FromArgb(228, 243, 240),
            SelectionForeColor = Color.FromArgb(25, 45, 42),
            Padding = new Padding(6, 0, 6, 0),
            WrapMode = DataGridViewTriState.False
        };

        grid.Columns.Add(new DataGridViewImageColumn
        {
            Name = "Icon",
            HeaderText = string.Empty,
            Width = 46,
            ImageLayout = DataGridViewImageCellLayout.Zoom,
            ReadOnly = true,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                NullValue = null
            }
        });

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Name",
            HeaderText = "名称",
            Width = 230,
            ReadOnly = false,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Status",
            HeaderText = "状态",
            Width = 96,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Title",
            HeaderText = "窗口标题",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 160,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Process",
            HeaderText = "进程",
            Width = 140,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        grid.CellValidating += OnCellValidating;
        grid.CellEndEdit += OnCellEndEdit;
        grid.CellDoubleClick += OnCellDoubleClick;
        grid.CellMouseDown += OnCellMouseDown;
        grid.SelectionChanged += (_, _) => UpdateActionAvailability();

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add(new ToolStripMenuItem("显示窗口", null, (_, _) => InvokeShowSelected()));
        contextMenu.Items.Add(new ToolStripMenuItem("收纳窗口", null, (_, _) => InvokeHideSelected()));
        contextMenu.Items.Add(new ToolStripMenuItem("重命名", null, (_, _) => BeginRenameSelected()));
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(new ToolStripMenuItem("从列表移除", null, (_, _) => InvokeRemoveSelected()));
        contextMenu.Opening += (_, _) => UpdateContextMenuAvailability(contextMenu);
        grid.ContextMenuStrip = contextMenu;

        return grid;
    }

    private static Label BuildEmptyState()
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(130, 138, 146),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 11F),
            Text = "还没有收纳任何窗口\n\n点击「收纳窗口」，再点击桌面上的目标窗口\nSwallowIt 只隐藏窗口，不会结束背后的进程",
            Padding = new Padding(24)
        };
    }

    private static Panel BuildContentPanel(Control grid, Control emptyState)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(245, 247, 248),
            Padding = new Padding(16, 12, 16, 10)
        };

        panel.Controls.Add(grid);
        panel.Controls.Add(emptyState);
        emptyState.BringToFront();

        return panel;
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(8, 2, 0, 2)
        };

        var collectMenuItem = new ToolStripMenuItem("收纳窗口(&N)...", null, (_, _) => CollectRequested?.Invoke())
        {
            ShortcutKeys = Keys.Control | Keys.N
        };

        _autoStartMenuItem = new ToolStripMenuItem("开机自启(&S)")
        {
            CheckOnClick = true
        };
        _autoStartMenuItem.Click += OnAutoStartClicked;

        var exitMenuItem = new ToolStripMenuItem("退出(&X)", null, (_, _) => ExitRequested?.Invoke())
        {
            ShortcutKeys = Keys.Control | Keys.Q
        };

        var fileMenu = new ToolStripMenuItem("文件(&F)");
        fileMenu.DropDownItems.AddRange(
        [
            collectMenuItem,
            new ToolStripSeparator(),
            _autoStartMenuItem,
            new ToolStripSeparator(),
            exitMenuItem
        ]);
        fileMenu.DropDownOpening += (_, _) => _autoStartMenuItem.Checked = AutoStart.IsEnabled();

        _showSelectedMenuItem = new ToolStripMenuItem("显示选中(&R)", null, (_, _) => InvokeShowSelected());
        _hideSelectedMenuItem = new ToolStripMenuItem("收纳选中(&H)", null, (_, _) => InvokeHideSelected());
        _removeMenuItem = new ToolStripMenuItem("从列表移除(&D)", null, (_, _) => InvokeRemoveSelected());
        _showAllMenuItem = new ToolStripMenuItem("全部显示(&A)", null, (_, _) => ShowAllRequested?.Invoke());
        _hideAllMenuItem = new ToolStripMenuItem("全部收纳(&L)", null, (_, _) => HideAllRequested?.Invoke());

        var actionsMenu = new ToolStripMenuItem("操作(&O)");
        actionsMenu.DropDownItems.AddRange(
        [
            _showSelectedMenuItem,
            _hideSelectedMenuItem,
            new ToolStripSeparator(),
            _showAllMenuItem,
            _hideAllMenuItem,
            new ToolStripSeparator(),
            _removeMenuItem
        ]);
        actionsMenu.DropDownOpening += (_, _) => UpdateActionAvailability();

        var aboutMenuItem = new ToolStripMenuItem("关于 SwallowIt(&A)", null, (_, _) => ShowAbout());
        var helpMenu = new ToolStripMenuItem("帮助(&H)");
        helpMenu.DropDownItems.Add(aboutMenuItem);

        menu.Items.AddRange([fileMenu, actionsMenu, helpMenu]);
        return menu;
    }

    private Panel BuildHeader()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(20, 12, 20, 8)
        };

        var title = new Label
        {
            Text = "SwallowIt",
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold),
            ForeColor = Color.FromArgb(15, 84, 80),
            Location = new Point(18, 8)
        };

        var subtitle = new Label
        {
            Text = "把本地服务窗口收好，用名字和图标找回来",
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 9F),
            ForeColor = Color.FromArgb(110, 118, 126),
            Location = new Point(22, 45)
        };

        var adminNote = new Label
        {
            Dock = DockStyle.Right,
            Width = 400,
            Text = "如果目标窗口以管理员身份运行，\nSwallowIt 也需要以管理员身份运行。",
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Microsoft YaHei UI", 9F),
            ForeColor = Color.FromArgb(146, 116, 74)
        };

        panel.Controls.Add(title);
        panel.Controls.Add(subtitle);
        panel.Controls.Add(adminNote);
        panel.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(233, 236, 239));
            e.Graphics.DrawLine(pen, 0, panel.Height - 1, panel.Width, panel.Height - 1);
        };

        return panel;
    }

    private Panel BuildActionBar()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(245, 247, 248),
            Padding = new Padding(16, 9, 16, 9)
        };

        _collectButton = CreateButton("收纳窗口", primary: true);
        _collectButton.Width = 118;
        _collectButton.Click += (_, _) => CollectRequested?.Invoke();

        _showButton = CreateButton("显示窗口");
        _showButton.Width = 88;
        _showButton.Click += (_, _) => InvokeShowSelected();

        _hideButton = CreateButton("隐藏窗口");
        _hideButton.Width = 88;
        _hideButton.Click += (_, _) => InvokeHideSelected();

        _removeButton = CreateButton("移除");
        _removeButton.Width = 72;
        _removeButton.Click += (_, _) => InvokeRemoveSelected();

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0, 7, 0, 7)
        };
        flow.Controls.Add(_collectButton);
        flow.Controls.Add(_showButton);
        flow.Controls.Add(_hideButton);
        flow.Controls.Add(_removeButton);

        _showAllButton = CreateButton("全部显示");
        _showAllButton.Width = 92;
        _showAllButton.Anchor = AnchorStyles.Right;
        _showAllButton.Click += (_, _) => ShowAllRequested?.Invoke();

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.Controls.Add(flow, 0, 0);
        layout.Controls.Add(_showAllButton, 1, 0);

        panel.Controls.Add(layout);
        return panel;
    }

    private static Button CreateButton(string text, bool primary = false)
    {
        var button = new Button
        {
            Text = text,
            Height = 32,
            Width = 84,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 0, 8, 0),
            Cursor = Cursors.Hand,
            Font = new Font("Microsoft YaHei UI", 9F),
            UseVisualStyleBackColor = false
        };

        if (primary)
        {
            button.BackColor = Color.FromArgb(13, 148, 136);
            button.ForeColor = Color.White;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(20, 168, 155);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(11, 128, 118);
        }
        else
        {
            button.BackColor = Color.White;
            button.ForeColor = Color.FromArgb(55, 65, 81);
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(211, 219, 224);
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(240, 245, 245);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(230, 238, 237);
        }

        return button;
    }

    private void AddEntryRowCore(ServiceEntry entry)
    {
        var rowIndex = _grid.Rows.Add(
            entry.Icon ?? SystemIcons.Application.ToBitmap(),
            entry.Name,
            entry.StateText,
            entry.CurrentTitle,
            entry.ProcessName);
        var row = _grid.Rows[rowIndex];
        row.Tag = entry;
        ApplyStatusStyle(row);
    }

    private DataGridViewRow? FindRow(ServiceEntry entry)
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (ReferenceEquals(row.Tag, entry))
            {
                return row;
            }
        }

        return null;
    }

    private ServiceEntry? GetSelectedEntry()
    {
        return _grid.CurrentRow?.Tag as ServiceEntry;
    }

    private void ApplyStatusStyle(DataGridViewRow row)
    {
        if (row.Tag is not ServiceEntry entry)
        {
            return;
        }

        var statusCell = row.Cells["Status"];
        statusCell.Style.ForeColor = entry.State switch
        {
            EntryState.Collected => Color.FromArgb(13, 126, 117),
            EntryState.Shown => Color.FromArgb(88, 100, 112),
            _ => Color.FromArgb(190, 92, 84)
        };

        row.Cells["Process"].Style.ForeColor = Color.FromArgb(120, 128, 136);
        row.Cells["Title"].Style.ForeColor = entry.State == EntryState.Gone
            ? Color.FromArgb(160, 166, 172)
            : Color.FromArgb(90, 98, 106);
    }

    private void UpdateSummary()
    {
        var entries = _grid.Rows
            .Cast<DataGridViewRow>()
            .Select(row => row.Tag)
            .OfType<ServiceEntry>()
            .ToList();

        var collected = entries.Count(entry => entry.State == EntryState.Collected);
        var shown = entries.Count(entry => entry.State == EntryState.Shown);
        var gone = entries.Count(entry => entry.State == EntryState.Gone);

        var summary = $"共 {entries.Count} 个 · 已收纳 {collected} · 已显示 {shown}";
        if (gone > 0)
        {
            summary += $" · 已消失 {gone}";
        }

        _summaryLabel.Text = summary;

        _emptyState.Visible = entries.Count == 0;
        if (_emptyState.Visible)
        {
            _emptyState.BringToFront();
        }

        UpdateActionAvailability();
    }

    private void UpdateActionAvailability()
    {
        var entry = GetSelectedEntry();
        var hasSelection = entry is not null;

        if (_showButton is not null)
        {
            _showButton.Enabled = hasSelection && entry!.State != EntryState.Gone;
            _hideButton.Enabled = hasSelection && entry!.State == EntryState.Shown;
            _removeButton.Enabled = hasSelection;
            _collectButton.Enabled = true;
        }

        if (_showSelectedMenuItem is not null)
        {
            _showSelectedMenuItem.Enabled = hasSelection && entry!.State != EntryState.Gone;
            _hideSelectedMenuItem.Enabled = hasSelection && entry!.State == EntryState.Shown;
            _removeMenuItem.Enabled = hasSelection;

            var entries = _grid.Rows
                .Cast<DataGridViewRow>()
                .Select(row => row.Tag)
                .OfType<ServiceEntry>()
                .ToList();

            _showAllMenuItem.Enabled = entries.Any(item => item.State == EntryState.Collected);
            _hideAllMenuItem.Enabled = entries.Any(item => item.State == EntryState.Shown);

            if (_showAllButton is not null)
            {
                _showAllButton.Enabled = _showAllMenuItem.Enabled;
            }
        }
    }

    private void UpdateContextMenuAvailability(ContextMenuStrip menu)
    {
        var entry = GetSelectedEntry();
        var isShown = entry?.State == EntryState.Shown;
        var isUsable = entry is not null && entry.State != EntryState.Gone;

        menu.Items[0].Enabled = isUsable;
        menu.Items[1].Enabled = isShown;
        menu.Items[2].Enabled = entry is not null;
        menu.Items[4].Enabled = entry is not null;
    }

    private void InvokeShowSelected()
    {
        if (GetSelectedEntry() is { } entry)
        {
            ShowEntryRequested?.Invoke(entry);
        }
    }

    private void InvokeHideSelected()
    {
        if (GetSelectedEntry() is { } entry)
        {
            HideEntryRequested?.Invoke(entry);
        }
    }

    private void InvokeRemoveSelected()
    {
        if (GetSelectedEntry() is { } entry)
        {
            RemoveEntryRequested?.Invoke(entry);
        }
    }

    private void BeginRenameSelected()
    {
        if (GetSelectedEntry() is not { } entry)
        {
            return;
        }

        var row = FindRow(entry);
        if (row is null)
        {
            return;
        }

        _grid.CurrentCell = row.Cells["Name"];
        _grid.BeginEdit(selectAll: true);
    }

    private void OnCellValidating(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (_grid.Columns[e.ColumnIndex].Name != "Name")
        {
            return;
        }

        var row = _grid.Rows[e.RowIndex];
        if (string.IsNullOrWhiteSpace(e.FormattedValue as string))
        {
            e.Cancel = true;
            row.ErrorText = "名称不能为空";
        }
        else
        {
            row.ErrorText = string.Empty;
        }
    }

    private void OnCellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "Name")
        {
            return;
        }

        var row = _grid.Rows[e.RowIndex];
        if (row.Tag is not ServiceEntry entry)
        {
            return;
        }

        var value = (row.Cells[e.ColumnIndex].Value as string ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            value = entry.Name;
        }

        row.Cells[e.ColumnIndex].Value = value;
        if (value == entry.Name)
        {
            return;
        }

        entry.Name = value;
        RenameCommitted?.Invoke(entry);
    }

    private void OnCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _grid.Columns[e.ColumnIndex].Name == "Name")
        {
            return;
        }

        if (_grid.Rows[e.RowIndex].Tag is not ServiceEntry entry)
        {
            return;
        }

        if (entry.State == EntryState.Collected)
        {
            ShowEntryRequested?.Invoke(entry);
        }
        else if (entry.State == EntryState.Shown)
        {
            HideEntryRequested?.Invoke(entry);
        }
    }

    private void OnCellMouseDown(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right && e.RowIndex >= 0)
        {
            var columnIndex = e.ColumnIndex >= 0 ? e.ColumnIndex : 0;
            _grid.CurrentCell = _grid.Rows[e.RowIndex].Cells[columnIndex];
            _grid.Rows[e.RowIndex].Selected = true;
        }
    }

    private void OnAutoStartClicked(object? sender, EventArgs e)
    {
        var enabled = _autoStartMenuItem.Checked;

        try
        {
            AutoStart.SetEnabled(enabled);
        }
        catch (Exception exception)
        {
            _autoStartMenuItem.Checked = !enabled;
            AppLog.Write("设置开机自启失败", exception);
            AppDialog.ShowError(this,
                "设置开机自启失败。\n\n详细信息已记录到日志文件。",
                WindowTitle);
        }
    }

    private void ShowAbout()
    {
        AppDialog.ShowInfo(this,
            "SwallowIt 0.1.0\n" +
            "作者：小李同学32q\n\n" +
            "把本地服务窗口收进托盘面板、命名，并在需要时恢复。\n" +
            "不会启动、停止或重启服务，也不会结束窗口背后的进程。\n\n" +
            "运行环境：.NET 10 桌面运行时（Windows 桌面运行时 x64）\n" +
            $"下载地址：{RuntimeDownloadUrl}\n\n" +
            "如果目标窗口以管理员身份运行，SwallowIt 也需要以管理员身份运行才能操作它。\n\n" +
            $"错误日志：{AppLog.FilePath}",
            "关于 SwallowIt",
            "打开 .NET 10 桌面运行时下载页面",
            RuntimeDownloadUrl);
    }

    private sealed class SmoothDataGridView : DataGridView
    {
        public SmoothDataGridView()
        {
            DoubleBuffered = true;
        }
    }
}
