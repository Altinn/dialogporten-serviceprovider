namespace Digdir.BDB.Dialogporten.ServiceProvider.Playbook.Dsl;

// Small expression + effect-statement language used by Phase B playbook DSL features.
// Grammar:
//   expr      ::= or-expr
//   or-expr   ::= and-expr ('||' and-expr)*
//   and-expr  ::= not-expr ('&&' not-expr)*
//   not-expr  ::= '!' not-expr | cmp-expr
//   cmp-expr  ::= add-expr (('==' | '!=' | '<' | '<=' | '>' | '>=') add-expr)?
//   add-expr  ::= mul-expr (('+' | '-') mul-expr)*
//   mul-expr  ::= atom (('*' | '/') atom)*
//   atom      ::= number | string | bool | identifier | identifier 'contains' atom | '(' expr ')'
//
//   stmt      ::= 'set' ident '=' expr
//               | 'inc' ident ('by' expr)?
//               | 'dec' ident ('by' expr)?
//               | 'add' ident '+=' expr
//               | 'remove' ident '-=' expr
//
// Values are int (long), bool, string, or list-of-the-same.

public sealed class ScriptException(string message) : Exception(message);

public abstract record Expr;
public sealed record IntLit(long Value) : Expr;
public sealed record BoolLit(bool Value) : Expr;
public sealed record StringLit(string Value) : Expr;
public sealed record VarRef(string Name) : Expr;
public sealed record BinOp(string Op, Expr Left, Expr Right) : Expr;
public sealed record UnaryNot(Expr Operand) : Expr;
public sealed record Contains(string ListVar, Expr Value) : Expr;

public abstract record Stmt;
public sealed record SetStmt(string Var, Expr Value) : Stmt;
public sealed record IncStmt(string Var, Expr? By) : Stmt;
public sealed record DecStmt(string Var, Expr? By) : Stmt;
public sealed record ListAddStmt(string Var, Expr Value) : Stmt;
public sealed record ListRemoveStmt(string Var, Expr Value) : Stmt;
public sealed record RollStmt(string Var, RollSpec Spec) : Stmt;

public abstract record RollSpec;
public sealed record DiceSpec(int Count, int Sides) : RollSpec;
public sealed record RangeSpec(long Lo, long Hi) : RollSpec;

internal enum TokKind { Int, Bool, String, Ident, Op, Lparen, Rparen, Dice, Range, Eof }
internal sealed record Tok(TokKind Kind, string Text, int Pos);

internal static class Lexer
{
    private static readonly string[] MultiCharOps = { "==", "!=", "<=", ">=", "&&", "||", "+=", "-=" };

    public static List<Tok> Tokenize(string source)
    {
        var toks = new List<Tok>();
        var i = 0;
        while (i < source.Length)
        {
            var c = source[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (c == '(') { toks.Add(new Tok(TokKind.Lparen, "(", i)); i++; continue; }
            if (c == ')') { toks.Add(new Tok(TokKind.Rparen, ")", i)); i++; continue; }

            if (c == '"')
            {
                var start = i; i++;
                var sb = new System.Text.StringBuilder();
                while (i < source.Length && source[i] != '"')
                {
                    if (source[i] == '\\' && i + 1 < source.Length)
                    {
                        sb.Append(source[i + 1] switch
                        {
                            'n' => '\n',
                            't' => '\t',
                            '\\' => '\\',
                            '"' => '"',
                            _ => source[i + 1]
                        });
                        i += 2;
                    }
                    else
                    {
                        sb.Append(source[i]); i++;
                    }
                }
                if (i >= source.Length) throw new ScriptException($"unterminated string starting at position {start}");
                i++; // closing quote
                toks.Add(new Tok(TokKind.String, sb.ToString(), start));
                continue;
            }

            if (char.IsDigit(c))
            {
                var start = i;
                while (i < source.Length && char.IsDigit(source[i])) i++;
                // Dice: 1d6, 2d20 — digit-run 'd' digit-run, no whitespace
                if (i < source.Length && (source[i] == 'd' || source[i] == 'D')
                    && i + 1 < source.Length && char.IsDigit(source[i + 1]))
                {
                    i++; // 'd'
                    while (i < source.Length && char.IsDigit(source[i])) i++;
                    toks.Add(new Tok(TokKind.Dice, source[start..i], start));
                    continue;
                }
                // Range: 1..6 — digit-run '..' digit-run
                if (i + 2 < source.Length && source[i] == '.' && source[i + 1] == '.' && char.IsDigit(source[i + 2]))
                {
                    i += 2; // '..'
                    while (i < source.Length && char.IsDigit(source[i])) i++;
                    toks.Add(new Tok(TokKind.Range, source[start..i], start));
                    continue;
                }
                toks.Add(new Tok(TokKind.Int, source[start..i], start));
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_')) i++;
                var text = source[start..i];
                if (text == "true" || text == "false") toks.Add(new Tok(TokKind.Bool, text, start));
                else toks.Add(new Tok(TokKind.Ident, text, start));
                continue;
            }

            // Try multi-char ops
            var matched = false;
            foreach (var op in MultiCharOps)
            {
                if (i + op.Length <= source.Length && source.Substring(i, op.Length) == op)
                {
                    toks.Add(new Tok(TokKind.Op, op, i));
                    i += op.Length;
                    matched = true;
                    break;
                }
            }
            if (matched) continue;

            // Single-char op
            if ("+-*/=!<>".IndexOf(c) >= 0)
            {
                toks.Add(new Tok(TokKind.Op, c.ToString(), i));
                i++;
                continue;
            }

            throw new ScriptException($"unexpected character '{c}' at position {i}");
        }
        toks.Add(new Tok(TokKind.Eof, "", source.Length));
        return toks;
    }
}

internal sealed class Parser(List<Tok> tokens)
{
    private int _i;

    private Tok Peek(int offset = 0) => tokens[Math.Min(_i + offset, tokens.Count - 1)];
    private Tok Take() => tokens[_i++];
    private bool Match(TokKind kind, string? text = null)
    {
        var t = Peek();
        if (t.Kind != kind) return false;
        if (text != null && t.Text != text) return false;
        _i++;
        return true;
    }
    private Tok Expect(TokKind kind, string? text = null)
    {
        var t = Peek();
        if (t.Kind != kind || (text != null && t.Text != text))
        {
            var want = text ?? kind.ToString();
            throw new ScriptException($"expected '{want}' but got '{t.Text}' at position {t.Pos}");
        }
        return Take();
    }

    public Expr ParseExpression()
    {
        var e = ParseOr();
        if (Peek().Kind != TokKind.Eof) throw new ScriptException($"unexpected '{Peek().Text}' at position {Peek().Pos}");
        return e;
    }

    public Stmt ParseStatement()
    {
        var t = Peek();
        if (t.Kind != TokKind.Ident) throw new ScriptException($"expected statement verb at position {t.Pos}");
        Stmt s = t.Text switch
        {
            "set" => ParseSet(),
            "inc" => ParseIncDec(positive: true),
            "dec" => ParseIncDec(positive: false),
            "add" => ParseListAdd(),
            "remove" => ParseListRemove(),
            "roll" => ParseRoll(),
            _ => throw new ScriptException($"unknown statement verb '{t.Text}'")
        };
        if (Peek().Kind != TokKind.Eof) throw new ScriptException($"unexpected '{Peek().Text}' at position {Peek().Pos}");
        return s;
    }

    private Stmt ParseSet()
    {
        Take(); // 'set'
        var ident = Expect(TokKind.Ident).Text;
        Expect(TokKind.Op, "=");
        var expr = ParseOr();
        return new SetStmt(ident, expr);
    }

    private Stmt ParseIncDec(bool positive)
    {
        Take(); // 'inc' or 'dec'
        var ident = Expect(TokKind.Ident).Text;
        Expr? by = null;
        if (Peek().Kind == TokKind.Ident && Peek().Text == "by")
        {
            Take();
            by = ParseOr();
        }
        return positive ? new IncStmt(ident, by) : new DecStmt(ident, by);
    }

    private Stmt ParseListAdd()
    {
        Take(); // 'add'
        var ident = Expect(TokKind.Ident).Text;
        Expect(TokKind.Op, "+=");
        var expr = ParseOr();
        return new ListAddStmt(ident, expr);
    }

    private Stmt ParseListRemove()
    {
        Take(); // 'remove'
        var ident = Expect(TokKind.Ident).Text;
        Expect(TokKind.Op, "-=");
        var expr = ParseOr();
        return new ListRemoveStmt(ident, expr);
    }

    private Stmt ParseRoll()
    {
        Take(); // 'roll'
        var ident = Expect(TokKind.Ident).Text;
        Expect(TokKind.Op, "=");
        var t = Peek();
        if (t.Kind == TokKind.Dice)
        {
            Take();
            var parts = t.Text.Split(new[] { 'd', 'D' }, 2);
            var count = int.Parse(parts[0]);
            var sides = int.Parse(parts[1]);
            if (count < 1) throw new ScriptException($"dice count must be >= 1 (got {count})");
            if (sides < 2) throw new ScriptException($"dice sides must be >= 2 (got {sides})");
            return new RollStmt(ident, new DiceSpec(count, sides));
        }
        if (t.Kind == TokKind.Range)
        {
            Take();
            var parts = t.Text.Split("..", 2, StringSplitOptions.None);
            var lo = long.Parse(parts[0]);
            var hi = long.Parse(parts[1]);
            if (lo > hi) throw new ScriptException($"range lo ({lo}) must be <= hi ({hi})");
            return new RollStmt(ident, new RangeSpec(lo, hi));
        }
        throw new ScriptException($"expected dice (e.g. 1d6) or range (e.g. 1..6) after '=' in roll, got '{t.Text}'");
    }

    private Expr ParseOr()
    {
        var left = ParseAnd();
        while (Peek().Kind == TokKind.Op && Peek().Text == "||")
        {
            Take();
            var right = ParseAnd();
            left = new BinOp("||", left, right);
        }
        return left;
    }

    private Expr ParseAnd()
    {
        var left = ParseNot();
        while (Peek().Kind == TokKind.Op && Peek().Text == "&&")
        {
            Take();
            var right = ParseNot();
            left = new BinOp("&&", left, right);
        }
        return left;
    }

    private Expr ParseNot()
    {
        if (Peek().Kind == TokKind.Op && Peek().Text == "!")
        {
            Take();
            return new UnaryNot(ParseNot());
        }
        return ParseCmp();
    }

    private Expr ParseCmp()
    {
        var left = ParseAdd();
        var t = Peek();
        if (t.Kind == TokKind.Op && t.Text is "==" or "!=" or "<" or "<=" or ">" or ">=")
        {
            Take();
            var right = ParseAdd();
            return new BinOp(t.Text, left, right);
        }
        return left;
    }

    private Expr ParseAdd()
    {
        var left = ParseMul();
        while (Peek().Kind == TokKind.Op && (Peek().Text == "+" || Peek().Text == "-"))
        {
            var op = Take().Text;
            var right = ParseMul();
            left = new BinOp(op, left, right);
        }
        return left;
    }

    private Expr ParseMul()
    {
        var left = ParseAtom();
        while (Peek().Kind == TokKind.Op && (Peek().Text == "*" || Peek().Text == "/"))
        {
            var op = Take().Text;
            var right = ParseAtom();
            left = new BinOp(op, left, right);
        }
        return left;
    }

    private Expr ParseAtom()
    {
        var t = Peek();
        if (Match(TokKind.Lparen))
        {
            var e = ParseOr();
            Expect(TokKind.Rparen);
            return e;
        }
        if (t.Kind == TokKind.Int)
        {
            Take();
            return new IntLit(long.Parse(t.Text));
        }
        if (t.Kind == TokKind.Bool)
        {
            Take();
            return new BoolLit(t.Text == "true");
        }
        if (t.Kind == TokKind.String)
        {
            Take();
            return new StringLit(t.Text);
        }
        if (t.Kind == TokKind.Ident)
        {
            Take();
            // 'contains' is a postfix operator on an identifier (list)
            if (Peek().Kind == TokKind.Ident && Peek().Text == "contains")
            {
                Take();
                var rhs = ParseAtom();
                return new Contains(t.Text, rhs);
            }
            return new VarRef(t.Text);
        }
        throw new ScriptException($"unexpected token '{t.Text}' at position {t.Pos}");
    }
}

public static class ScriptParser
{
    public static Expr ParseExpression(string source)
    {
        var toks = Lexer.Tokenize(source);
        return new Parser(toks).ParseExpression();
    }

    public static Stmt ParseStatement(string source)
    {
        var toks = Lexer.Tokenize(source);
        return new Parser(toks).ParseStatement();
    }
}

public static class Evaluator
{
    public static object? Evaluate(Expr expr, IReadOnlyDictionary<string, object?> vars) => expr switch
    {
        IntLit i => i.Value,
        BoolLit b => b.Value,
        StringLit s => s.Value,
        VarRef v => LookupVar(v.Name, vars),
        UnaryNot n => !ToBool(Evaluate(n.Operand, vars)),
        Contains c => EvalContains(c, vars),
        BinOp b => EvalBin(b, vars),
        _ => throw new ScriptException($"cannot evaluate {expr}")
    };

    private static object? LookupVar(string name, IReadOnlyDictionary<string, object?> vars)
    {
        if (!vars.TryGetValue(name, out var v)) throw new ScriptException($"undefined variable '{name}'");
        return v;
    }

    private static object? EvalBin(BinOp b, IReadOnlyDictionary<string, object?> vars)
    {
        var l = Evaluate(b.Left, vars);
        // Short-circuit for logical ops
        if (b.Op == "&&")
        {
            if (!ToBool(l)) return false;
            return ToBool(Evaluate(b.Right, vars));
        }
        if (b.Op == "||")
        {
            if (ToBool(l)) return true;
            return ToBool(Evaluate(b.Right, vars));
        }
        var r = Evaluate(b.Right, vars);
        return b.Op switch
        {
            "==" => ValueEquals(l, r),
            "!=" => !ValueEquals(l, r),
            "<" => ToLong(l) < ToLong(r),
            "<=" => ToLong(l) <= ToLong(r),
            ">" => ToLong(l) > ToLong(r),
            ">=" => ToLong(l) >= ToLong(r),
            "+" => ToLong(l) + ToLong(r),
            "-" => ToLong(l) - ToLong(r),
            "*" => ToLong(l) * ToLong(r),
            "/" => ToLong(r) == 0 ? throw new ScriptException("division by zero") : ToLong(l) / ToLong(r),
            _ => throw new ScriptException($"unknown operator '{b.Op}'")
        };
    }

    private static bool EvalContains(Contains c, IReadOnlyDictionary<string, object?> vars)
    {
        var listVar = LookupVar(c.ListVar, vars);
        if (listVar is not System.Collections.IList list) throw new ScriptException($"'{c.ListVar}' is not a list");
        var needle = Evaluate(c.Value, vars);
        foreach (var item in list)
        {
            if (ValueEquals(item, needle)) return true;
        }
        return false;
    }

    public static IReadOnlyDictionary<string, object?> Apply(Stmt stmt, IReadOnlyDictionary<string, object?> vars, Random? rng = null)
    {
        var next = new Dictionary<string, object?>(vars);
        switch (stmt)
        {
            case SetStmt s:
                EnsureDeclared(next, s.Var);
                next[s.Var] = Evaluate(s.Value, next);
                break;
            case IncStmt inc:
                EnsureDeclared(next, inc.Var);
                next[inc.Var] = ToLong(next[inc.Var]) + (inc.By != null ? ToLong(Evaluate(inc.By, next)) : 1);
                break;
            case DecStmt dec:
                EnsureDeclared(next, dec.Var);
                next[dec.Var] = ToLong(next[dec.Var]) - (dec.By != null ? ToLong(Evaluate(dec.By, next)) : 1);
                break;
            case ListAddStmt add:
                {
                    EnsureDeclared(next, add.Var);
                    var list = ToList(next[add.Var]);
                    var newList = new List<object?>(list) { Evaluate(add.Value, next) };
                    next[add.Var] = newList;
                    break;
                }
            case ListRemoveStmt rem:
                {
                    EnsureDeclared(next, rem.Var);
                    var list = ToList(next[rem.Var]);
                    var needle = Evaluate(rem.Value, next);
                    var newList = list.Where(x => !ValueEquals(x, needle)).ToList();
                    next[rem.Var] = newList;
                    break;
                }
            case RollStmt roll:
                {
                    EnsureDeclared(next, roll.Var);
                    if (rng is null) throw new ScriptException("roll requires an RNG (Phase C). None was supplied.");
                    next[roll.Var] = roll.Spec switch
                    {
                        DiceSpec d => RollDice(d.Count, d.Sides, rng),
                        RangeSpec r => rng.NextInt64(r.Lo, r.Hi + 1),
                        _ => throw new ScriptException($"unknown roll spec {roll.Spec}")
                    };
                    break;
                }
            default:
                throw new ScriptException($"cannot apply {stmt}");
        }
        return next;
    }

    private static long RollDice(int count, int sides, Random rng)
    {
        long total = 0;
        for (var i = 0; i < count; i++) total += rng.Next(1, sides + 1);
        return total;
    }

    private static void EnsureDeclared(Dictionary<string, object?> vars, string name)
    {
        if (!vars.ContainsKey(name)) throw new ScriptException($"undeclared variable '{name}'");
    }

    public static long ToLong(object? v) => v switch
    {
        long l => l,
        int i => i,
        bool b => b ? 1 : 0,
        _ => throw new ScriptException($"expected int, got {DescribeType(v)}")
    };

    public static bool ToBool(object? v) => v switch
    {
        bool b => b,
        _ => throw new ScriptException($"expected bool, got {DescribeType(v)}")
    };

    public static IReadOnlyList<object?> ToList(object? v) => v switch
    {
        System.Collections.IList list => list.Cast<object?>().ToList(),
        _ => throw new ScriptException($"expected list, got {DescribeType(v)}")
    };

    public static bool ValueEquals(object? a, object? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        if (a is long al && b is long bl) return al == bl;
        if (a is int ai && b is long bl2) return ai == bl2;
        if (a is long al2 && b is int bi) return al2 == bi;
        if (a is bool ab && b is bool bb) return ab == bb;
        if (a is string asv && b is string bs) return asv == bs;
        return a.Equals(b);
    }

    private static string DescribeType(object? v) => v switch
    {
        null => "null",
        long => "int",
        int => "int",
        bool => "bool",
        string => "string",
        System.Collections.IList => "list",
        _ => v.GetType().Name
    };

    private static readonly System.Text.RegularExpressions.Regex VarPlaceholder =
        new(@"\{vars\.([A-Za-z_][A-Za-z0-9_]*)\}", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Replace <c>{vars.X}</c> placeholders in <paramref name="source"/> with the stringified value
    /// of session variable <c>X</c>. Unknown var names are left as-is (acts as a "passthrough" so
    /// callers can spot mistakes by seeing the literal token in their dialog).
    /// </summary>
    public static string Interpolate(string source, IReadOnlyDictionary<string, object?> vars)
    {
        if (!source.Contains("{vars.")) return source;
        return VarPlaceholder.Replace(source, m =>
        {
            var name = m.Groups[1].Value;
            return vars.TryGetValue(name, out var v) ? FormatVarForDisplay(v) : m.Value;
        });
    }

    public static string FormatVarForDisplay(object? v) => v switch
    {
        null => "",
        bool b => b ? "true" : "false",
        long l => l.ToString(System.Globalization.CultureInfo.InvariantCulture),
        int i => i.ToString(System.Globalization.CultureInfo.InvariantCulture),
        string s => s,
        System.Collections.IList list => string.Join(", ", list.Cast<object?>().Select(FormatVarForDisplay)),
        _ => v.ToString() ?? ""
    };
}
