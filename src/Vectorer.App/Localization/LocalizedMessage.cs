namespace Vectorer.App.Localization;

/// <summary>
/// Represents a localizable message with a resource key and snapshot arguments,
/// or an explicit literal string that should not be translated.
/// </summary>
public sealed record LocalizedMessage
{
    public string? Key { get; }
    public object?[] Arguments { get; }
    public string? LiteralText { get; }
    public bool IsLiteral => LiteralText is not null;

    public LocalizedMessage(string key, params object?[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Key = key;
        Arguments = arguments;
    }

    private LocalizedMessage(string literalText, bool _)
    {
        LiteralText = literalText;
        Arguments = [];
    }

    public static LocalizedMessage Literal(string text) => new(text ?? string.Empty, true);
}
