using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArayCode.Services;
using Spectre.Console;
using StreamShell;

namespace ArayCode;

/// <summary>
/// Composes text messages from user input and attachments.
/// Extracted from StreamShellInputHandler to honor Single Responsibility.
/// </summary>
public sealed class TextMessageComposer
{
    private const int MaxAttachmentLines = 300;
    private const int MaxAttachmentChars = 5000;

    private readonly IStreamShellHost _host;
    private readonly ITextMessageSender _textSender;

    public TextMessageComposer(IStreamShellHost host, ITextMessageSender textSender)
    {
        _host = host;
        _textSender = textSender;
    }

    public bool TryToComposeMessage(string input, IReadOnlyList<Attachment>? attachments, out string? result)
    {
        var message = ComposeMessage(input, attachments);

        if (string.IsNullOrWhiteSpace(message))
        {
            result = null;
            return false;
        }

        result = message.Trim();
        return true;
    }

    /// <summary>
    /// Composes the message by prepending attachment content, then sends it.
    /// Returns true if the message was sent successfully.
    /// </summary>
    public async Task<bool> SendWithAttachmentsAsync(string message, CancellationToken ct)
    {
        try
        {
            await _textSender.SendAsync(message, ct);
            return true;
        }
        catch (Exception ex)
        {
            _host.AddMessage($"[red]  Failed to send message: {Markup.Escape(ex.Message)}[/]");
            return false;
        }
    }

    /// <summary>
    /// Composes message text from user input and attachment content.
    /// Truncates attachment content to prevent flooding.
    /// </summary>
    public static string ComposeMessage(string input, IReadOnlyList<Attachment>? attachments)
    {
        var message = input;

        if (attachments == null || attachments.Count == 0)
            return message;

        var attachmentTexts = new List<string>();
        foreach (var attachment in attachments)
        {
            var text = attachment.Content;
            // Truncate to MaxAttachmentLines or MaxAttachmentChars, whichever is fewer
            var lines = text.Split('\n');
            if (lines.Length > MaxAttachmentLines)
                text = string.Join("\n", lines.Take(MaxAttachmentLines)) + "\n...";
            if (text.Length > MaxAttachmentChars)
                text = text[..MaxAttachmentChars] + "...";
            attachmentTexts.Add(text);
        }

        var attachmentPrefix = string.Join("\n", attachmentTexts);
        return string.IsNullOrWhiteSpace(message)
            ? attachmentPrefix
            : attachmentPrefix + "\n" + message;
    }
}
