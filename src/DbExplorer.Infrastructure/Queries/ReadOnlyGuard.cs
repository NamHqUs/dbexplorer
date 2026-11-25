namespace DbExplorer.Infrastructure.Queries;

// Guard for read-only connections. It is a conservative tokenizer, not a T-SQL parser:
// every statement must start with SELECT, WITH, or DECLARE, and no write, DDL, or EXEC keyword may appear
// outside comments, strings, and quoted identifiers. It prevents accidents; it is not a security boundary.
public static class ReadOnlyGuard
{
    private static readonly HashSet<string> AllowedStatementStarts = ["SELECT", "WITH", "DECLARE"];

    private static readonly HashSet<string> ForbiddenKeywords =
    [
        "INSERT", "UPDATE", "DELETE", "MERGE", "INTO", "DROP", "ALTER", "CREATE", "TRUNCATE",
        "EXEC", "EXECUTE", "GRANT", "REVOKE", "DENY", "BULK", "OPENROWSET", "OPENQUERY",
        "OPENDATASOURCE", "SHUTDOWN", "DBCC", "USE", "SET", "BACKUP", "RESTORE", "KILL", "WAITFOR",
    ];

    // Returns the first keyword that is not allowed, or null when the SQL is read-only.
    public static string? FindViolation(string sql)
    {
        var atStatementStart = true;
        foreach (var token in Tokenize(sql))
        {
            if (token.Kind == TokenKind.Semicolon)
            {
                atStatementStart = true;
                continue;
            }

            if (token.Kind != TokenKind.Word)
            {
                atStatementStart = false;
                continue;
            }

            var word = token.Text.ToUpperInvariant();
            if (atStatementStart && !AllowedStatementStarts.Contains(word)) return word;
            if (ForbiddenKeywords.Contains(word)) return word;
            atStatementStart = false;
        }
        return null;
    }

    private enum TokenKind { Word, Semicolon, Other }

    private readonly record struct Token(TokenKind Kind, string Text);

    private static IEnumerable<Token> Tokenize(string sql)
    {
        var i = 0;
        while (i < sql.Length)
        {
            var c = sql[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c == '-' && Peek(sql, i + 1) == '-')
            {
                while (i < sql.Length && sql[i] != '\n') i++;
            }
            else if (c == '/' && Peek(sql, i + 1) == '*')
            {
                i = SkipBlockComment(sql, i);
            }
            else if (c == '\'' || c == '"' || c == '[')
            {
                // Strings and quoted identifiers are opaque: keywords inside them do not count.
                var close = c == '[' ? ']' : c;
                i = SkipDelimited(sql, i, close);
                yield return new Token(TokenKind.Other, string.Empty);
            }
            else if (c == ';')
            {
                yield return new Token(TokenKind.Semicolon, ";");
                i++;
            }
            else if (IsWordStart(c))
            {
                var start = i;
                while (i < sql.Length && IsWordPart(sql[i])) i++;
                yield return new Token(TokenKind.Word, sql[start..i]);
            }
            else
            {
                if (char.IsDigit(c))
                {
                    while (i < sql.Length && char.IsLetterOrDigit(sql[i])) i++;
                }
                else
                {
                    i++;
                }
                yield return new Token(TokenKind.Other, string.Empty);
            }
        }
    }

    private static char Peek(string sql, int index) => index < sql.Length ? sql[index] : '\0';

    private static bool IsWordStart(char c) => char.IsLetter(c) || c is '_' or '@' or '#';

    private static bool IsWordPart(char c) => char.IsLetterOrDigit(c) || c is '_' or '@' or '#' or '$';

    // SQL Server block comments nest, so the depth is tracked.
    private static int SkipBlockComment(string sql, int start)
    {
        var depth = 0;
        var i = start;
        while (i < sql.Length)
        {
            if (sql[i] == '/' && Peek(sql, i + 1) == '*')
            {
                depth++;
                i += 2;
            }
            else if (sql[i] == '*' && Peek(sql, i + 1) == '/')
            {
                depth--;
                i += 2;
                if (depth == 0) return i;
            }
            else
            {
                i++;
            }
        }
        return i;
    }

    // A doubled closing character is an escaped quote ('it''s', "a""b", [a]]b]).
    private static int SkipDelimited(string sql, int start, char close)
    {
        var i = start + 1;
        while (i < sql.Length)
        {
            if (sql[i] == close)
            {
                if (Peek(sql, i + 1) == close)
                {
                    i += 2;
                    continue;
                }
                return i + 1;
            }
            i++;
        }
        return i;
    }
}
