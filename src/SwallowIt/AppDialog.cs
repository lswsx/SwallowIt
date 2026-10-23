using System.Diagnostics;

namespace SwallowIt;

internal static class AppDialog
{
    private const string DefaultTitle = "SwallowIt";

    public static void ShowInfo(
        IWin32Window? owner,
        string text,
        string? title = null,
        string? linkText = null,
        string? linkUrl = null)
    {
        Show(owner, text, title ?? DefaultTitle, MessageBoxIcon.Information,
            "确定", DialogResult.OK, null, linkText, linkUrl);
    }

    public static void ShowWarning(IWin32Window? owner, string text, string? title = null)
    {
        Show(owner, text, title ?? DefaultTitle, MessageBoxIcon.Warning,
            "确定", DialogResult.OK, null, null, null);
    }

    public static void ShowError(IWin32Window? owner, string text, string? title = null)
    {
        Show(owner, text, title ?? DefaultTitle, MessageBoxIcon.Error,
            "确定", DialogResult.OK, null, null, null);
    }

    public static bool Confirm(
        IWin32Window? owner,
        string text,
        string? title = null,
        string acceptText = "是",
        string cancelText = "否")
    {
        var result = Show(owner, text, title ?? DefaultTitle, MessageBoxIcon.Question,
            acceptText, DialogResult.Yes, cancelText, null, null);
        return result == DialogResult.Yes;
    }

    private static DialogResult Show(
        IWin32Window? owner,
        string text,
        string title,
        MessageBoxIcon icon,
        string acceptText,
        DialogResult? acceptResult,
        string? cancelText,
        string? linkText,
        string? linkUrl)
    {
        using var dialog = new AppDialogForm(
            text, title, icon, acceptText, acceptResult, cancelText, linkText, linkUrl)
        {
            TopMost = owner is null,
            StartPosition = owner is null
                ? FormStartPosition.CenterScreen
                : FormStartPosition.CenterParent
        };

        return owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
    }
}

internal sealed class AppDialogForm : Form
{
    private readonly Label _message;
    private readonly LinkLabel? _link;
    private readonly FlowLayoutPanel _buttons;

    public AppDialogForm(
        string text,
        string title,
        MessageBoxIcon icon,
        string acceptText,
        DialogResult? acceptResult,
        string? cancelText,
        string? linkText,
        string? linkUrl)
    {
        Text = title;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ShowIcon = false;
        BackColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 9F);

        var margin = 24;
        var iconSize = 48;
        var gap = 16;

        var iconBox = new PictureBox
        {
            Image = GetIcon(icon).ToBitmap(),
            SizeMode = PictureBoxSizeMode.StretchImage,
            Size = new Size(iconSize, iconSize),
            Location = new Point(margin, margin)
        };

        _message = new Label
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(520, 0),
            Font = new Font("Microsoft YaHei UI", 9.5F),
            ForeColor = Color.FromArgb(32, 38, 45),
            UseMnemonic = false,
            Location = new Point(margin + iconSize + gap, margin + 2)
        };

        Controls.Add(iconBox);
        Controls.Add(_message);

        var contentBottom = margin + Math.Max(iconSize, _message.Height);

        if (!string.IsNullOrWhiteSpace(linkText) && !string.IsNullOrWhiteSpace(linkUrl))
        {
            _link = new LinkLabel
            {
                Text = linkText,
                AutoSize = true,
                LinkColor = Color.FromArgb(0, 112, 192),
                ActiveLinkColor = Color.FromArgb(0, 84, 144),
                UseMnemonic = false,
                Location = new Point(_message.Left, contentBottom + 10)
            };
            _link.LinkClicked += (_, _) => OpenLink(linkUrl);
            Controls.Add(_link);
            contentBottom = _link.Bottom;
        }

        _buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };

        var accept = CreateButton(acceptText, acceptResult ?? DialogResult.OK);
        _buttons.Controls.Add(accept);
        AcceptButton = accept;

        if (!string.IsNullOrWhiteSpace(cancelText))
        {
            var cancel = CreateButton(cancelText, DialogResult.Cancel);
            _buttons.Controls.Add(cancel);
            CancelButton = cancel;
        }
        else
        {
            CancelButton = accept;
        }

        Controls.Add(_buttons);

        var contentRight = Math.Max(iconBox.Right, _message.Right);
        if (_link is not null)
        {
            contentRight = Math.Max(contentRight, _link.Right);
        }

        var width = Math.Max(360, contentRight + margin);
        var height = contentBottom + 22 + 34 + 20;
        ClientSize = new Size(width, height);

        _buttons.Location = new Point(
            ClientSize.Width - margin - _buttons.Width,
            ClientSize.Height - 20 - _buttons.Height);
    }

    private Button CreateButton(string text, DialogResult result)
    {
        return new Button
        {
            Text = text,
            DialogResult = result,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(92, 34),
            Padding = new Padding(10, 0, 10, 0),
            UseMnemonic = false
        };
    }

    private void OpenLink(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            AppLog.Write("打开下载页面失败", exception);
            AppDialog.ShowError(this, "无法打开浏览器，请手动复制下面的地址到浏览器中访问：\n" + url);
        }
    }

    private static Icon GetIcon(MessageBoxIcon icon) => icon switch
    {
        MessageBoxIcon.Error => SystemIcons.Error,
        MessageBoxIcon.Warning => SystemIcons.Warning,
        MessageBoxIcon.Question => SystemIcons.Question,
        _ => SystemIcons.Information
    };
}
