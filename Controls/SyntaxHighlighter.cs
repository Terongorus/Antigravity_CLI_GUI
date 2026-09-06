using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TeronClaudeCodeVS.Controls
{
    /// <summary>What color family a span of code text should read as - mapped to a real Visual
    /// Studio editor classification by <see cref="Core.ClaudeCodePackage.GetClassificationForeground"/>,
    /// not a hand-picked palette, so it tracks the user's actual theme/Fonts-and-Colors setup.</summary>
    internal enum TokenKind { Plain, Keyword, String, Comment, Number }

    internal readonly struct SyntaxToken(string text, TokenKind kind)
    {
        public string Text { get; } = text;
        public TokenKind Kind { get; } = kind;
    }

    /// <summary>
    /// Deliberately not a real language service (no AvalonEdit/ColorCode dependency pulled in for
    /// this): a single generic comment/string/number/keyword regex per language profile, close
    /// enough for a chat transcript's code blocks without taking on a new VSIX dependency or a
    /// real tokenizer's maintenance burden. Unknown/unlisted languages fall back to one Plain
    /// token - callers already handle that as "no highlighting," matching today's behavior.
    /// </summary>
    internal static class SyntaxHighlighter
    {
        private sealed class LanguageProfile
        {
            public LanguageProfile(IEnumerable<string> keywords, string? lineComment,
                (string Open, string Close)? blockComment, string quoteChars)
            {
                Keywords = new HashSet<string>(keywords, StringComparer.Ordinal);
                Pattern = BuildPattern(lineComment, blockComment, quoteChars);
            }

            public HashSet<string> Keywords { get; }
            public Regex Pattern { get; }
        }

        public static IReadOnlyList<SyntaxToken> Tokenize(string? language, string code)
        {
            LanguageProfile? profile = ResolveProfile(language);
            if (profile == null || string.IsNullOrEmpty(code))
                return [new SyntaxToken(code, TokenKind.Plain)];

            List<SyntaxToken> tokens = [];
            int last = 0;

            foreach (Match m in profile.Pattern.Matches(code))
            {
                if (m.Index > last)
                    tokens.Add(new SyntaxToken(code.Substring(last, m.Index - last), TokenKind.Plain));

                tokens.Add(new SyntaxToken(m.Value, ClassifyMatch(m, profile)));
                last = m.Index + m.Length;
            }

            if (last < code.Length)
                tokens.Add(new SyntaxToken(code.Substring(last), TokenKind.Plain));

            return tokens;
        }

        private static TokenKind ClassifyMatch(Match m, LanguageProfile profile)
        {
            if (m.Groups["blockcomment"].Success || m.Groups["linecomment"].Success) return TokenKind.Comment;
            if (m.Groups["string"].Success) return TokenKind.String;
            if (m.Groups["number"].Success) return TokenKind.Number;
            if (m.Groups["word"].Success) return profile.Keywords.Contains(m.Value) ? TokenKind.Keyword : TokenKind.Plain;
            return TokenKind.Plain;
        }

        private static Regex BuildPattern(string? lineComment, (string Open, string Close)? blockComment, string quoteChars)
        {
            List<string> alternatives = [];

            if (blockComment is { } bc)
                alternatives.Add($"(?<blockcomment>{Regex.Escape(bc.Open)}.*?{Regex.Escape(bc.Close)})");

            if (lineComment != null)
                alternatives.Add($"(?<linecomment>{Regex.Escape(lineComment)}.*?$)");

            if (quoteChars.Length > 0)
            {
                string stringAlts = string.Join("|", quoteChars.Select(q =>
                {
                    string qs = Regex.Escape(q.ToString());
                    return $"{qs}(?:\\\\.|[^{qs}\\\\])*{qs}";
                }));
                alternatives.Add($"(?<string>{stringAlts})");
            }

            alternatives.Add(@"(?<number>\b\d+(?:\.\d+)?\b)");
            alternatives.Add(@"(?<word>[A-Za-z_][A-Za-z0-9_]*)");

            return new Regex(string.Join("|", alternatives),
                RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.Singleline);
        }

        // ─── Language profiles ──────────────────────────────────────────────────────

        private static readonly string[] s_csharpKeywords =
        [
            "abstract","as","async","await","base","bool","break","byte","case","catch","char","checked",
            "class","const","continue","decimal","default","delegate","do","double","else","enum","event",
            "explicit","extern","false","finally","fixed","float","for","foreach","get","global","goto",
            "if","implicit","in","init","int","interface","internal","is","lock","long","namespace","new",
            "null","object","operator","out","override","params","partial","private","protected","public",
            "readonly","record","ref","required","return","sbyte","sealed","set","short","sizeof","stackalloc",
            "static","string","struct","switch","this","throw","true","try","typeof","uint","ulong","unchecked",
            "unsafe","ushort","using","value","var","virtual","void","volatile","when","where","while","yield",
        ];

        private static readonly string[] s_javaScriptKeywords =
        [
            "abstract","any","as","asserts","async","await","boolean","break","case","catch","class","const",
            "continue","debugger","declare","default","delete","do","else","enum","export","extends","false",
            "finally","for","from","function","get","if","implements","import","in","instanceof","interface",
            "is","keyof","let","module","namespace","new","null","number","object","of","package","private",
            "protected","public","readonly","return","set","static","string","super","switch","symbol","this",
            "throw","true","try","type","typeof","undefined","var","void","while","with","yield",
        ];

        private static readonly string[] s_pythonKeywords =
        [
            "and","as","assert","async","await","break","class","continue","def","del","elif","else","except",
            "False","finally","for","from","global","if","import","in","is","lambda","None","nonlocal","not",
            "or","pass","raise","return","True","try","while","with","yield","self",
        ];

        private static readonly string[] s_bashKeywords =
        [
            "if","then","elif","else","fi","for","while","until","do","done","case","esac","function","return",
            "local","export","readonly","declare","echo","exit","break","continue","in","select","time","shift",
            "unset","trap","source","alias","true","false",
        ];

        private static readonly string[] s_jsonKeywords = ["true", "false", "null"];

        private static readonly string[] s_powerShellKeywords =
        [
            "begin","break","catch","class","continue","data","define","do","dynamicparam","else","elseif",
            "end","enum","exit","filter","finally","for","foreach","from","function","if","in","param","process",
            "return","switch","throw","trap","try","until","using","var","while","true","false","null",
        ];

        private static readonly LanguageProfile s_csharp = new(s_csharpKeywords, "//", ("/*", "*/"), "\"");
        private static readonly LanguageProfile s_javaScript = new(s_javaScriptKeywords, "//", ("/*", "*/"), "\"'`");
        private static readonly LanguageProfile s_python = new(s_pythonKeywords, "#", null, "\"'");
        private static readonly LanguageProfile s_bash = new(s_bashKeywords, "#", null, "\"'");
        private static readonly LanguageProfile s_json = new(s_jsonKeywords, null, null, "\"");
        private static readonly LanguageProfile s_powerShell = new(s_powerShellKeywords, "#", ("<#", "#>"), "\"'");

        private static LanguageProfile? ResolveProfile(string? language) => language?.Trim().ToLowerInvariant() switch
        {
            "csharp" or "cs" or "c#" => s_csharp,
            "java" or "go" or "golang" or "rust" or "rs" or "cpp" or "c++" or "c" or "kotlin" or "kt" or "swift" => s_csharp,
            "javascript" or "js" or "jsx" or "typescript" or "ts" or "tsx" or "mjs" or "cjs" => s_javaScript,
            "python" or "py" => s_python,
            "bash" or "sh" or "shell" or "zsh" => s_bash,
            "json" or "jsonc" => s_json,
            "powershell" or "ps1" or "pwsh" => s_powerShell,
            _ => null,
        };
    }
}
