using System.Windows;
using Microsoft.Win32;

namespace WindowsCustomizer.App.Infrastructure;

/// <summary>
/// Structured user-facing error (spec: Error Handling).
/// Every error dialog answers: what happened, why, and what the user can do next.
/// </summary>
public sealed record UserError(string What, string Why, string WhatToDo)
{
    public static UserError FromException(Exception ex, string what, string? whatToDo = null)
    {
        var why = ex.Message;
        if (ex is System.ComponentModel.Win32Exception win32)
        {
            why += $" (Win32 error {win32.NativeErrorCode})";
        }

        return new UserError(what, why, whatToDo ??
            "Check the operation log for details, then retry. If the problem persists, verify the source file is valid and not locked by another process.");
    }
}

public interface IDialogService
{
    void ShowInfo(string title, string message);

    void ShowWarning(string title, string message);

    void ShowError(string title, UserError error);

    /// <summary>Returns true when the user confirms.</summary>
    bool Confirm(string title, string message);

    /// <summary>Returns the selected file path or null.</summary>
    string? OpenFile(string title, string filter);

    /// <summary>Returns the selected folder path or null.</summary>
    string? OpenFolder(string title);

    /// <summary>Asks the user for a single line of text. Returns null on cancel.</summary>
    string? AskText(string title, string prompt, string defaultValue = "");
}

/// <summary>WPF implementation using standard dialogs (theme-agnostic, always available).</summary>
public sealed class DialogService : IDialogService
{
    public void ShowInfo(string title, string message)
        => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void ShowWarning(string title, string message)
        => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public void ShowError(string title, UserError error)
    {
        var text =
            $"What happened?\n{error.What}\n\n" +
            $"Why did it happen?\n{error.Why}\n\n" +
            $"What can you do?\n{error.WhatToDo}";
        MessageBox.Show(text, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public bool Confirm(string title, string message)
        => MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public string? OpenFile(string title, string filter)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true,
            Multiselect = false
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? OpenFolder(string title)
    {
        // .NET 8+ ships a native folder picker for WPF.
        var dialog = new OpenFolderDialog
        {
            Title = title
        };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public string? AskText(string title, string prompt, string defaultValue = "")
    {
        var input = new System.Windows.Controls.TextBox
        {
            Text = defaultValue,
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(6, 4, 6, 4)
        };

        var ok = new System.Windows.Controls.Button
        {
            Content = "OK",
            IsDefault = true,
            Width = 84,
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(8, 4, 8, 4)
        };
        var cancel = new System.Windows.Controls.Button
        {
            Content = "Cancel",
            IsCancel = true,
            Width = 84,
            Padding = new Thickness(8, 4, 8, 4)
        };

        var buttons = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var panel = new System.Windows.Controls.StackPanel();
        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = prompt,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(input);
        panel.Children.Add(buttons);

        var window = new Window
        {
            Title = title,
            Content = panel,
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 420,
            MaxWidth = 560,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Padding = new Thickness(18)
        };

        ok.Click += (_, _) => { window.DialogResult = true; };
        cancel.Click += (_, _) => { window.DialogResult = false; };
        window.Loaded += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
        };

        return window.ShowDialog() == true ? input.Text : null;
    }
}
