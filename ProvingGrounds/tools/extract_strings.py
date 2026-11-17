"""Writes fidelity/strings.json: every player-visible string literal of the original C++ (gate G2, 00-overview §6).

    python3 tools/extract_strings.py              # write fidelity/strings.json, print a summary by kind and by file
    python3 tools/extract_strings.py --check      # exit 1 if fidelity/strings.json is not what the script would write
    python3 tools/extract_strings.py --sample N --seed S
                                                  # also print N kept and N left-out literals, picked with seed S (review aid)
    python3 tools/extract_strings.py --rest       # also list every left-out literal that no exclusion rule explains

Output: [{"text", "file", "line", "kind"}] sorted by file, then line, then text. `text` is the literal's value after C escape
processing (UTF-8 source bytes and \\x escapes decoded as UTF-8), with adjacent literals joined; printf formats keep their %
codes. `file` is relative to upstream/ProvingGrounds/plugin/src, `line` is the literal's first line. `kind` is the sink the text
reaches (setCaption, setOnlyText, showPlayerAMessage, say, tooltip, Label, Button, WrappedLabel, ...) or, for literals that
only the table rule keeps, the table's name.

How a literal is judged visible: a backwards taint analysis over the whole plugin, on a real C++ token stream (comments,
character literals and preprocessor lines are skipped; a BOM is ignored).
  * Seeds: the text argument of every call to a sink (SINKS), and the PrisonerUtil.cpp speech table. The plugin's own
    wrappers (NativeUI::Label/Button/WrappedLabel, SetStatus, Feedback, Report, Blocked, ...) are not listed: they are found
    because their parameter reaches a sink, and their call sites are labelled with the sink (or, for Label/Button/TextBox/
    WrappedLabel, with the wrapper's name). setUserString(key, text) is followed for every key the code reads back with
    getUserString(key) inside a tainted expression (the tooltips), labelled `tooltip`.
  * From a tainted expression: its string literals are kept, except those compared with ==/!=, inside the condition of a
    ?: , passed to a function in NOT_TEXT or a log function, used in the same file as a JSON key, or shaped like an id
    (not_text_shape). Identifiers are followed: a local to its assignments, +=, append/assign/insert/push_back/swap,
    stream <<, sprintf-style writes and writes through out-parameters (`T&` / `T*`) in its function; a global to its writes
    in its file (then in the headers it includes); a member (x.f, x->f, a.b.f, it->second.f) to the writes `.f =`
    of the struct the receiver's declared type names (typedefs expanded; all structs with f when the type cannot be told),
    to constructor initializer lists, in-class defaults and the f column of aggregate tables; a call to a plugin function
    to its return expressions (and to writes through it when it returns a reference); a parameter to the matching argument
    at every call site. Calls are resolved by name, arity and #include visibility, members only to class members, virtual
    calls also to overrides in derived classes. Names whose declared type cannot carry text (numbers, bools, pointers to
    widgets or characters) are not followed.
  * Tables: every literal of TownAnnouncerPolicy.h, TownSpectators.cpp and TownLimbShopPolicy.h is kept unless an exclusion
    rule says it is not text (record stringIDs, logs, comparisons, JSON keys) or it is an sprintf argument (then the taint
    analysis decides whether that buffer is shown).
Left out: log ids and log text (PGLog::*, CombatBalanceLog::*, ...), JSON keys, file names, widget skin/type names and widget
names, layer names, user-string keys, record stringIDs, stable ids (armour.warlord.cloak), and literals of functions no sink
reaches (dead code such as TownChallengePolicy::Rarity and TownLimbShop::GetStatus).
Known limit: parameters are context-insensitive, so a helper's parameter reached through one caller marks the arguments of
all its callers (this over-includes a few sidecar error texts, e.g. ArenaSnapshotFiles.cpp "Reading").
The script is deterministic: no clock, sorted inputs, a seeded RNG only for --sample.
"""
import json, random, re, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "upstream/ProvingGrounds/plugin/src"
OUT = ROOT / "fidelity/strings.json"

# Calls whose argument a player sees: name -> index of the text argument.
SINKS = {"setCaption": 0, "setCaptionWithReplacing": 0, "setOnlyText": 0, "addItem": 0, "showPlayerAMessage": 0, "say": 0}
# Wrappers named in the plan's sink list: their call sites are labelled with the wrapper's own name.
NAMED_WRAPPERS = {"Label", "Button", "TextBox", "WrappedLabel"}
# Calls whose literal arguments are keys, ids, lookups or log text, never shown (checked inside tainted expressions).
NOT_TEXT = {
    "getUserString", "setUserString", "findWidget", "find", "rfind", "find_first_of", "find_last_of", "find_first_not_of",
    "find_last_not_of", "compare", "strcmp", "strncmp", "_stricmp", "_strnicmp", "stricmp", "strstr", "Equals", "HasMember",
    "FindMember", "getDataByName", "getData", "GetDataByName", "getReferenceListIfExists", "createWidget", "createWidgetReal",
    "createWidgetT", "Field", "Member", "Key", "Index", "getStringID", "GetStringId", "atoi", "atof", "strtol", "strtoul",
    "strtod", "GetModuleHandleA", "GetProcAddress", "setTextAlign", "setFontName", "setImageTexture", "setImageResource",
    "setSkin", "changeWidgetSkin", "attachToLayer", "loadLayout", "OpponentKey", "sizeof", "Quote",
}
# JSON object keys (excluded even where a validation message carries the key path): which argument holds the key.
JSON_KEY_ARG0 = {"Key", "HasMember", "FindMember", "AddMember", "Field"}
JSON_KEY_ARG1 = {"Member", "At"}
JSON_WRITER_HELPERS = {"Int", "Uint", "Number", "Bool", "String", "BoolArray", "Double", "Float", "Array"}
# Log sinks: their text goes to pg_*.log files, never to the player.
LOG_QUALIFIERS = {"PGLog", "CombatBalanceLog"}
LOG_FUNCS = {"Log", "LogLifecycle", "LogNpcFormationProgress", "RewardDiag", "OutputDebugStringA", "OutputDebugStringW",
             "DiagnosticEvent", "MarketEvent", "ChallengeEvent", "Event", "AbortMatch", "ChallengeLog", "Trace"}
# Methods that return their receiver's text (so the receiver is followed).
PASSTHROUGH = {"c_str", "str", "substr", "asUTF8", "asUTF8_c_str", "data", "front", "back", "at", "toString", "asWStr"}
# Writers: first argument is the buffer, the rest is what is written into it.
WRITERS = {"snprintf", "_snprintf", "sprintf", "sprintf_s", "_snprintf_s", "vsnprintf", "_vsnprintf", "swprintf", "strcpy",
           "strcpy_s", "strncpy", "strncpy_s", "strcat", "strcat_s", "strncat", "strncat_s", "StringCchPrintfA",
           "StringCchCopyA", "wsprintfA"}
APPENDERS = {"append", "assign", "insert", "push_back", "emplace_back", "swap", "replace"}
NUMERIC = set("""
int float double bool unsigned signed long short char size_t DWORD UINT BOOL WORD BYTE LONG ULONG ULONGLONG uint8_t uint16_t
uint32_t uint64_t int8_t int16_t int32_t int64_t __int64 ptrdiff_t time_t const static volatile inline constexpr mutable
register void
""".split())
KEYWORDS = set("""
alignas alignof and asm auto bool break case catch char char16_t char32_t class const constexpr const_cast continue decltype
default delete do double dynamic_cast else enum explicit export extern false float for friend goto if inline int long mutable
namespace new noexcept not nullptr operator or private protected public register reinterpret_cast return short signed sizeof
static static_assert static_cast struct switch template this thread_local throw true try typedef typeid typename union unsigned
using virtual void volatile wchar_t while override final NULL size_t DWORD BOOL
""".split())
CALL_PREFIX_KEYWORDS = {"return", "else", "case", "do", "throw", "new", "delete", "co_return", "and", "or", "not"}
# Literals that only the table rule keeps (00-overview: the speech and script tables).
TABLE_FILES = {"TownAnnouncerPolicy.h": "TownAnnouncerPolicy", "TownSpectators.cpp": "TownSpectators",
               "TownLimbShopPolicy.h": "TownLimbShopPolicy"}
TABLE_ARRAYS = {("PrisonerUtil.cpp", "kPrisonerFightLines"): "kPrisonerFightLines"}

STRING_ID_RE = re.compile(r"^\d+-[^\s]*\.(mod|base)$|^\d+-[A-Za-z ]+\.(mod|base)$")
PUNCT = sorted("""
>>= <<= ->* ... :: -> ++ -- << >> <= >= == != && || += -= *= /= %= &= |= ^= ## .* { } [ ] ( ) ; : , . ? ~ ! + - * / % ^ & | = < > #
""".split(), key=len, reverse=True)


class Tok:
    __slots__ = ("kind", "text", "line", "wide", "raw")

    def __init__(self, kind, text, line, wide=False, raw=""):
        self.kind, self.text, self.line, self.wide, self.raw = kind, text, line, wide, raw

    def __repr__(self):
        return f"{self.kind}:{self.text!r}@{self.line}"


ESCAPES = {"n": b"\n", "t": b"\t", "r": b"\r", "\\": b"\\", "'": b"'", '"': b'"', "?": b"?", "a": b"\a", "b": b"\b",
           "f": b"\f", "v": b"\v"}


def decode_body(body, path, line):
    """C escape rules on the text between the quotes; returns bytes."""
    out = bytearray()
    i = 0
    while i < len(body):
        c = body[i]
        if c != "\\":
            out += c.encode("utf-8")
            i += 1
            continue
        i += 1
        if i >= len(body):
            sys.exit(f"{path}:{line}: dangling backslash in a string literal")
        c = body[i]
        if c == "\n":
            i += 1
        elif c in ESCAPES:
            out += ESCAPES[c]
            i += 1
        elif c == "x":
            m = re.match(r"[0-9a-fA-F]+", body[i + 1:])
            if not m:
                sys.exit(f"{path}:{line}: bad \\x escape")
            out.append(int(m.group(0), 16) & 0xFF)
            i += 1 + len(m.group(0))
        elif c in "01234567":
            m = re.match(r"[0-7]{1,3}", body[i:])
            out.append(int(m.group(0), 8) & 0xFF)
            i += len(m.group(0))
        elif c in "uU":
            n = 4 if c == "u" else 8
            out += chr(int(body[i + 1:i + 1 + n], 16)).encode("utf-8")
            i += 1 + n
        else:
            sys.exit(f"{path}:{line}: unknown escape \\{c}")
    return bytes(out)


def lex(path):
    s = path.read_text(encoding="utf-8-sig")
    toks = []
    i, n, line = 0, len(s), 1
    at_line_start = True
    while i < n:
        c = s[i]
        if c == "\n":
            line += 1
            i += 1
            at_line_start = True
            continue
        if c in " \t\r\f\v":
            i += 1
            continue
        if s.startswith("//", i):
            while i < n and s[i] != "\n":
                if s[i] == "\\" and i + 1 < n and s[i + 1] == "\n":
                    line += 1
                    i += 1
                i += 1
            continue
        if s.startswith("/*", i):
            j = s.find("*/", i + 2)
            if j < 0:
                sys.exit(f"{path}:{line}: unterminated comment")
            line += s.count("\n", i, j)
            i = j + 2
            continue
        if c == "#" and at_line_start:  # preprocessor directive (includes, pragmas, macros): skipped whole
            while i < n and s[i] != "\n":
                if s.startswith("/*", i):
                    j = s.find("*/", i + 2)
                    line += s.count("\n", i, j)
                    i = j + 2
                    continue
                if s.startswith("//", i):
                    while i < n and s[i] != "\n":
                        i += 1
                    break
                if s[i] == "\\" and i + 1 < n and s[i + 1] in "\r\n":
                    i += 1
                    if s[i] == "\r":
                        i += 1
                    line += 1
                i += 1
            continue
        at_line_start = False
        m = re.match(r'(u8|u|U|L)?R"([^ ()\\\t\v\f\n]{0,16})\(', s[i:i + 40])
        if m:  # raw string literal
            end = s.find(")" + m.group(2) + '"', i + m.end())
            if end < 0:
                sys.exit(f"{path}:{line}: unterminated raw string")
            body = s[i + m.end():end]
            toks.append(Tok("str", body, line, wide=bool(m.group(1)), raw=s[i:end + len(m.group(2)) + 2]))
            line += body.count("\n")
            i = end + len(m.group(2)) + 2
            continue
        m = re.match(r'(u8|u|U|L)?"', s[i:i + 3])
        if m:
            j = i + m.end()
            start_line = line
            while True:
                if j >= n or s[j] == "\n":
                    sys.exit(f"{path}:{line}: unterminated string")
                if s[j] == "\\":
                    if s[j + 1] == "\n":
                        line += 1
                    j += 2
                    continue
                if s[j] == '"':
                    break
                j += 1
            body = s[i + m.end():j]
            value = decode_body(body, path, start_line).decode("utf-8")
            toks.append(Tok("str", value, start_line, wide=bool(m.group(1)), raw=s[i:j + 1]))
            i = j + 1
            continue
        m = re.match(r"(u8|u|U|L)?'", s[i:i + 3])
        if m and not (toks and toks[-1].kind == "num" and s[i - 1].isalnum()):
            j = i + m.end()
            while j < n and s[j] != "'":
                if s[j] == "\n":
                    sys.exit(f"{path}:{line}: unterminated character literal")
                j += 2 if s[j] == "\\" else 1
            toks.append(Tok("chr", s[i:j + 1], line))
            i = j + 1
            continue
        if c.isdigit() or (c == "." and i + 1 < n and s[i + 1].isdigit()):
            m = re.match(r"\.?[0-9](?:[eEpP][+-]|['0-9A-Za-z_.])*", s[i:])
            toks.append(Tok("num", m.group(0), line))
            i += len(m.group(0))
            continue
        if c.isalpha() or c == "_":
            m = re.match(r"[A-Za-z_][A-Za-z0-9_]*", s[i:])
            toks.append(Tok("id", m.group(0), line))
            i += len(m.group(0))
            continue
        for p in PUNCT:
            if s.startswith(p, i):
                toks.append(Tok("op", p, line))
                i += len(p)
                break
        else:
            sys.exit(f"{path}:{line}: unexpected character {c!r}")
    # Adjacent literals are one literal (translation phase 6).
    merged = []
    for t in toks:
        if t.kind == "str" and merged and merged[-1].kind == "str":
            prev = merged[-1]
            merged[-1] = Tok("str", prev.text + t.text, prev.line, prev.wide or t.wide, prev.raw + " " + t.raw)
        else:
            merged.append(t)
    return merged


TEXT_TYPES = {"string", "wstring", "char", "wchar_t", "UString", "ostringstream", "stringstream", "istringstream",
              "basic_string", "LPCSTR", "LPSTR", "auto"}


def cannot_hold_text(ids, star, array):
    """A declared type that can never carry a string literal: a number or bool, or a pointer to a non-text object
    (a widget, a character, a building)."""
    if not ids:
        return False
    if set(ids) <= NUMERIC:
        return not ("char" in ids and (star or array))
    if star:
        return not any(t in TEXT_TYPES for t in ids)
    return False


class Param:
    __slots__ = ("name", "out", "default", "numeric", "type_ids")

    def __init__(self, name, out, default, numeric, type_ids):
        self.name, self.out, self.default, self.numeric, self.type_ids = name, out, default, numeric, type_ids


class Func:
    """A function or constructor definition with a body."""

    def __init__(self, file, name, qual, scope, popen, pclose, bopen, bclose, params, ret_ids, ret_numeric, initlist):
        self.file, self.name, self.qual, self.scope = file, name, qual, scope  # scope: [(kind, name)], outermost first
        self.popen, self.pclose, self.bopen, self.bclose = popen, pclose, bopen, bclose
        self.params, self.ret_ids, self.ret_numeric = params, ret_ids, ret_numeric
        self.initlist = initlist  # [(member, start, end)] of a constructor's initializer list
        self.locals = {}  # name -> (numeric, type ids)
        self.cls = None  # class it is a member of (set by Analysis)

    def names(self):
        return [n for _, n in self.scope if n] + list(self.qual)

    def namespaces(self):
        return {n for kind, n in self.scope if kind == "namespace" and n} | set(self.qual)

    def anonymous(self):
        return any(kind == "namespace" and not n for kind, n in self.scope)


class Unit:
    """One source file: tokens, bracket matches, functions, structs and declarations."""

    def __init__(self, path):
        self.name = path.relative_to(SRC).as_posix()
        raw = path.read_text(encoding="utf-8-sig")
        self.includes = [m for m in re.findall(r'^\s*#\s*include\s*"([^"]+)"', raw, re.M) if (SRC / m).is_file()]
        self.toks = lex(path)
        self.match = {}
        stack = []
        for k, t in enumerate(self.toks):
            if t.kind == "op" and t.text in "([{":
                stack.append(k)
            elif t.kind == "op" and t.text in ")]}":
                if not stack:
                    sys.exit(f"{self.name}:{t.line}: unbalanced {t.text}")
                o = stack.pop()
                self.match[o], self.match[k] = k, o
        if stack:
            sys.exit(f"{self.name}: unbalanced brackets")
        self.ids = {}  # identifier -> token indices
        for k, t in enumerate(self.toks):
            if t.kind == "id":
                self.ids.setdefault(t.text, []).append(k)
        self.funcs = []
        self.structs = {}  # name -> [(field, numeric, type ids, default initializer range)]
        self.bases = {}  # struct -> [base class names]
        self.methods = {}  # struct -> {method names declared in its body}
        self.usings = set()
        self.func_at = [None] * len(self.toks)
        self.structure(0, len(self.toks), [])
        for f in self.funcs:
            for k in range(f.popen, f.bclose + 1):
                self.func_at[k] = f
        self.def_name_at = {f.popen - 1 for f in self.funcs}
        for f in self.funcs:
            f.locals = self.find_locals(f)
        self.globals = {}  # name -> (numeric, type ids), declarations outside function bodies
        for k, t in enumerate(self.toks):
            if t.kind == "id" and self.func_at[k] is None and k not in self.def_name_at and self.is_declared_at(k):
                self.globals.setdefault(t.text, self.decl_type(k))
        for k in self.ids.get("using", ()):
            if self.is_id(k + 1, "namespace") and self.is_id(k + 2):
                self.usings.add(self.toks[k + 2].text)
        self.declared = {self.toks[k].text for k, t in enumerate(self.toks)
                         if t.kind == "id" and self.is_op(k + 1, "(") and self.func_at[k] is None}
        self.json_keys = self.find_json_keys()
        self.typedefs = {}  # name -> type ids it stands for
        for k in self.ids.get("typedef", ()):
            e = k + 1
            while e < len(self.toks) and not self.is_op(e, ";"):
                e += 1
            body = self.toks[k + 1:e]
            if body and body[-1].kind == "id" and not any(t.kind == "op" and t.text == "(" for t in body):
                self.typedefs[body[-1].text] = [t.text for t in body[:-1] if t.kind == "id"]

    def find_json_keys(self):
        """Identifier-shaped literals this file uses as JSON object keys: obj["key"], Key/HasMember/FindMember/AddMember/
        Field("key", ...), Member(path, "key"), At(obj, "key", ...) and the writer helpers Int/Uint/Number/Bool/String/
        BoolArray(w, "key", ...)."""
        keys = set()
        for k, t in enumerate(self.toks):
            if t.kind != "str" or not re.fullmatch(r"[A-Za-z][A-Za-z0-9_]*", t.text):
                continue
            if self.is_op(k - 1, "[") and self.is_op(k + 1, "]"):
                keys.add(t.text)
                continue
            p = k - 1
            while p >= 0 and not self.is_op(p, "("):
                p -= 1
                if self.is_op(p) and self.toks[p].text in (";", "{", "}", ")"):
                    p = -1
            if p < 0:
                continue
            c = self.callee_of(p)
            if not c:
                continue
            args = self.split_commas(p + 1, self.match[p])
            pos = next((i for i, (a, e) in enumerate(args) if a <= k < e), None)
            if pos is None or args[pos] != (k, k + 1):
                continue
            name = c[0]
            if (name in JSON_KEY_ARG0 and pos == 0) or (name in JSON_KEY_ARG1 and pos == 1) or \
                    (name in JSON_WRITER_HELPERS and pos == 1 and self.is_id(args[0][0], "w")):
                keys.add(t.text)
        return keys

    # ---- token helpers ----
    def is_op(self, k, text=None):
        return 0 <= k < len(self.toks) and self.toks[k].kind == "op" and (text is None or self.toks[k].text == text)

    def is_id(self, k, text=None):
        return 0 <= k < len(self.toks) and self.toks[k].kind == "id" and (text is None or self.toks[k].text == text)

    def qualifier_before(self, k):
        """Names before toks[k] joined by ::, outermost first; and the index of the first token of the qualified name."""
        qual = []
        j = k
        while self.is_op(j - 1, "::") and self.is_id(j - 2):
            qual.insert(0, self.toks[j - 2].text)
            j -= 2
        return qual, j

    def split_commas(self, a, b):
        parts, s, k = [], a, a
        while k < b:
            t = self.toks[k]
            if t.kind == "op" and t.text in "([{" and k in self.match:
                k = self.match[k] + 1
                continue
            if t.kind == "op" and t.text == "<":
                close = self.template_close(k, b)
                if close:
                    k = close + 1
                    continue
            if t.kind == "op" and t.text == ",":
                parts.append((s, k))
                s = k + 1
            k += 1
        if s < b:
            parts.append((s, b))
        return parts

    def template_close(self, k, b):
        """Index of the > closing a template argument list opened at k, or None when < is a comparison."""
        if not self.is_id(k - 1) or self.toks[k - 1].text in KEYWORDS - {"static_cast", "const_cast", "reinterpret_cast",
                                                                          "dynamic_cast"}:
            return None
        depth, j = 0, k
        while j < b:
            t = self.toks[j]
            if t.kind == "op":
                if t.text == "<":
                    depth += 1
                elif t.text == ">":
                    depth -= 1
                    if depth == 0:
                        return j
                elif t.text == ">>":
                    depth -= 2
                    if depth <= 0:
                        return j
                elif t.text in (";", "{", "}", "&&", "||", "?", "=", "==", "!="):
                    return None
                elif t.text in "([":
                    j = self.match[j]
            elif t.kind in ("str", "num"):
                return None
            j += 1
        return None

    def expr_end(self, k, limit):
        """End (exclusive) of the expression starting at k: the first ; or , at depth 0, or an unmatched closer."""
        j = k
        while j < limit:
            t = self.toks[j]
            if t.kind == "op":
                if t.text in "([{":
                    j = self.match[j] + 1
                    continue
                if t.text in (";", ",") or t.text in ")]}":
                    return j
            j += 1
        return limit

    def callee_of(self, popen):
        """(name, qualifier, is a member call, name index) of the call whose ( is at popen, or None."""
        k = popen - 1
        if self.is_op(k, ">"):  # createWidget<MyGUI::Button>(...)
            depth, o = 0, k
            while o > 0:
                if self.is_op(o, ">"):
                    depth += 1
                elif self.is_op(o, "<"):
                    depth -= 1
                    if depth == 0:
                        break
                elif self.is_op(o) and self.toks[o].text in (";", "{", "}", "(", ")"):
                    return None
                o -= 1
            k = o - 1
        if not self.is_id(k):
            return None
        qual, first = self.qualifier_before(k)
        member = self.is_op(first - 1, ".") or self.is_op(first - 1, "->")
        return self.toks[k].text, qual, member, k

    # ---- structure ----
    def structure(self, a, b, scope):
        k = a
        while k < b:
            t = self.toks[k]
            if t.kind == "op" and t.text == "{":
                close = self.match[k]
                kind, info = self.classify_brace(k, scope)
                if kind == "function":
                    self.funcs.append(info(close))
                elif kind == "namespace":
                    self.structure(k + 1, close, scope + [("namespace", info)])
                elif kind == "class":
                    self.structs[info] = self.fields_of(k + 1, close)
                    self.bases[info] = self.bases_of(k)
                    self.methods[info] = self.methods_of(k + 1, close)
                    self.structure(k + 1, close, scope + [("class", info)])
                k = close + 1
                continue
            k += 1

    def skip_qualifiers_back(self, j):
        while self.is_id(j) and self.toks[j].text in ("const", "override", "noexcept", "final", "volatile", "mutable"):
            j -= 1
        return j

    def classify_brace(self, k, scope):
        j = self.skip_qualifiers_back(k - 1)
        initlist = []
        if self.is_op(j, ")"):
            ctor = self.constructor_before(j)
            if ctor:
                j, initlist = ctor
            popen = self.match[j]
            nk = popen - 1
            if self.is_id(nk) and self.toks[nk].text not in KEYWORDS | {"if", "for", "while", "switch", "catch"}:
                qual, first = self.qualifier_before(nk)
                if self.is_op(first - 1, "~"):
                    return "other", None  # destructor
                params = self.params_of(popen + 1, j)
                ret_ids, star = self.type_back(first - 1)
                name = self.toks[nk].text
                numeric = cannot_hold_text(ret_ids, star, False)
                pclose = j
                return "function", lambda close: Func(self.name, name, qual, list(scope), popen, pclose, k, close, params,
                                                      ret_ids, numeric, initlist)
            return "other", None
        if self.is_id(j - 1, "namespace"):
            return "namespace", self.toks[j].text if self.is_id(j) else ""
        if self.is_id(j, "namespace"):
            return "namespace", ""
        if j >= 0 and self.toks[j].kind == "str" and self.is_id(j - 1, "extern"):
            return "namespace", ""
        m = j
        while m >= 0 and not (self.toks[m].kind == "op" and self.toks[m].text in (";", "{", "}", ")", "=")):
            if self.is_id(m) and self.toks[m].text in ("struct", "class", "union"):
                n = m + 1
                return "class", self.toks[n].text if self.is_id(n) else ""
            if self.is_id(m, "enum"):
                return "other", None
            m -= 1
        return "other", None

    def bases_of(self, brace):
        """Base classes named between `class X :` and its {."""
        j = brace - 1
        while j >= 0 and not self.is_op(j, ":") and not (self.is_id(j) and self.toks[j].text in ("class", "struct")):
            j -= 1
        if not self.is_op(j, ":"):
            return []
        bases = []
        for s, e in self.split_commas(j + 1, brace):
            ids = [t.text for t in self.toks[s:e] if t.kind == "id" and t.text not in ("public", "private", "protected",
                                                                                     "virtual")]
            if ids:
                bases.append(ids[-1])
        return bases

    def methods_of(self, a, b):
        names, k = set(), a
        while k < b:
            if self.is_op(k, "{"):
                k = self.match[k] + 1
                continue
            if self.is_op(k, "(") and self.is_id(k - 1):
                names.add(self.toks[k - 1].text)
                k = self.match[k] + 1
                continue
            k += 1
        return names

    def constructor_before(self, j):
        """j is the ) before a body's {. If it closes the last entry of a constructor initializer list, return the index
        of the constructor's own ) and the entries [(member, start, end)]; else None."""
        entries = []
        q = j
        while True:
            if not (self.is_op(q, ")") or self.is_op(q, "}")):
                return None
            o = self.match[q]
            n = o - 1
            if not self.is_id(n):
                return None
            entries.insert(0, (self.toks[n].text, o + 1, q))
            before = n - 1
            if self.is_op(before, ","):
                q = before - 1
                continue
            if self.is_op(before, ":"):
                close = self.skip_qualifiers_back(before - 1)
                if self.is_op(close, ")") and len(entries) >= 1:
                    return close, entries
            return None

    def type_back(self, j):
        """Type names written before toks[j+1] (ids, outermost first) and whether a * appears."""
        ids, star = [], False
        while j >= 0:
            t = self.toks[j]
            if t.kind == "id":
                if t.text in CALL_PREFIX_KEYWORDS or t.text in ("public", "private", "protected", "case", "goto"):
                    break
                ids.insert(0, t.text)
            elif t.kind == "op" and t.text in ("*", "&", "&&", "::"):
                star = star or t.text == "*"
            elif t.kind == "op" and t.text == ">":
                depth, o = 0, j
                while o >= 0:
                    if self.is_op(o, ">"):
                        depth += 1
                    elif self.is_op(o, "<"):
                        depth -= 1
                        if depth == 0:
                            break
                    elif self.is_op(o) and self.toks[o].text in (";", "{", "}", "(", ")"):
                        return ids, star
                    elif self.is_id(o):
                        ids.insert(0, self.toks[o].text)
                    o -= 1
                j = o
            else:
                break
            j -= 1
            if len(ids) > 10:
                break
        return ids, star

    def params_of(self, a, b):
        params = []
        for s, e in self.split_commas(a, b):
            toks = self.toks[s:e]
            if not toks or (len(toks) == 1 and toks[0].text == "void"):
                continue
            eq = next((i for i, t in enumerate(toks) if t.kind == "op" and t.text == "="), len(toks))
            decl = toks[:eq]
            ids = [i for i, t in enumerate(decl) if t.kind == "id" and t.text not in KEYWORDS]
            name = decl[ids[-1]].text if ids and len(decl) > 1 else ""
            texts = [t.text for t in decl]
            type_ids = [t.text for t in decl if t.kind == "id" and t.text != name]
            star = "*" in texts or "[" in texts
            out = ("&" in texts or "*" in texts) and "const" not in texts
            numeric = cannot_hold_text(type_ids, "*" in texts, "[" in texts)
            params.append(Param(name, out, eq < len(toks), numeric, type_ids))
        return params

    def fields_of(self, a, b):
        fields = []
        k, start = a, a
        while k < b:
            t = self.toks[k]
            if t.kind == "op" and t.text == "{":
                close = self.match[k]
                j = self.skip_qualifiers_back(k - 1)
                k = close + 1
                if self.is_op(j, ")"):  # a member function body: no ; follows it
                    start = k
                continue  # an initializer or a nested type: its ; ends the declaration
            if t.kind == "op" and t.text == ";":
                decl = self.toks[start:k]
                texts = [x.text for x in decl]
                if decl and not any(x in ("static", "typedef", "using", "friend", "enum", "struct", "class", "union",
                                          "operator") for x in texts) and "(" not in texts:
                    type_ids, star = None, False
                    for s, e in self.split_commas(start, k):
                        part = self.toks[s:e]
                        eq = next((i for i, x in enumerate(part) if x.kind == "op" and x.text in ("=", "[", "{")),
                                  len(part))
                        ids = [x.text for x in part[:eq] if x.kind == "id"]
                        if not ids:
                            continue
                        if type_ids is None:
                            type_ids = ids[:-1]
                            star = any(x.kind == "op" and x.text == "*" for x in part[:eq])
                        array = eq < len(part) and part[eq].text == "["
                        numeric = cannot_hold_text(type_ids, star, array)
                        init = None
                        if eq < len(part) and part[eq].text in ("=", "{"):
                            init = (s + eq + 1, e) if part[eq].text == "=" else (s + eq + 1, s + eq + (e - s - eq) - 1)
                        fields.append((ids[-1], numeric, type_ids, init))
                start = k + 1
            elif t.kind == "op" and t.text == ":" and self.is_id(k - 1) and self.toks[k - 1].text in (
                    "public", "private", "protected"):
                start = k + 1
            k += 1
        return fields

    # ---- declarations ----
    def is_declared_at(self, k):
        t = self.toks[k]
        if t.kind != "id" or t.text in KEYWORDS:
            return False
        p = self.toks[k - 1] if k > 0 else None
        nx = self.toks[k + 1] if k + 1 < len(self.toks) else None
        if p is None or nx is None or nx.kind != "op" or nx.text not in ("=", ";", "(", "[", "{", ",", ":", ")"):
            return False
        if nx.text == ":" and k + 2 < len(self.toks) and self.toks[k + 2].kind == "op" and self.toks[k + 2].text != "(":
            return False
        if p.kind == "id":
            return p.text not in CALL_PREFIX_KEYWORDS and p.text not in (
                "goto", "public", "private", "protected", "namespace", "struct", "class", "union", "enum", "using",
                "typedef", "friend", "case")
        if p.kind == "op" and p.text in ("*", "&", "&&"):
            q = self.toks[k - 2]
            return (q.kind == "id" and q.text not in CALL_PREFIX_KEYWORDS) or (q.kind == "op" and q.text in (">", "*"))
        if p.kind == "op" and p.text == ">":
            o, depth = k - 1, 0
            while o > 0:
                if self.is_op(o, ">"):
                    depth += 1
                elif self.is_op(o, "<"):
                    depth -= 1
                    if depth == 0:
                        return self.is_id(o - 1)
                elif self.is_op(o) and self.toks[o].text in (";", "{", "}", "=", "(", ")"):
                    return False
                o -= 1
        return False

    def decl_type(self, k):
        """(numeric, type ids) of the declaration of toks[k]."""
        ids, star = self.type_back(k - 1)
        array = self.is_op(k + 1, "[")
        numeric = cannot_hold_text(ids, star, array)
        return numeric, ids

    def find_locals(self, f):
        names = {p.name: (p.numeric, p.type_ids) for p in f.params if p.name}
        k = f.bopen + 1
        while k < f.bclose:
            if self.is_declared_at(k):
                info = self.decl_type(k)
                names.setdefault(self.toks[k].text, info)
                # further declarators of the same declaration: int a = 0, b; std::string x, y;
                j = self.expr_end(k + 1, f.bclose) if not self.is_op(k + 1, ",") else k + 1
                while self.is_op(j, ","):
                    n = j + 1
                    while self.is_op(n) and self.toks[n].text in ("*", "&"):
                        n += 1
                    if self.is_id(n) and self.is_op(n + 1) and self.toks[n + 1].text in ("=", ";", ",", "(", "[", "{"):
                        names.setdefault(self.toks[n].text, info)
                        j = self.expr_end(n + 1, f.bclose) if not self.is_op(n + 1, ",") else n + 1
                    else:
                        break
            k += 1
        return names

    def decl_of(self, k, name):
        """(numeric, type ids) of the variable `name` as seen from toks[k], or None."""
        f = self.func_at[k]
        if f is not None and name in f.locals:
            return f.locals[name]
        return self.globals.get(name)


class Analysis:
    def __init__(self, units):
        self.units = units
        self.by_name = {u.name: u for u in units}
        self.structs = {}  # struct -> [(unit, fields)]
        for u in units:
            for s, fl in u.structs.items():
                if s:
                    self.structs.setdefault(s, []).append((u, fl))
        self.funcs_by_name = {}
        for u in units:
            for f in u.funcs:
                self.funcs_by_name.setdefault(f.name, []).append(f)
                if f.qual and f.qual[-1] in self.structs:
                    f.cls = f.qual[-1]
                elif f.scope and f.scope[-1][0] == "class":
                    f.cls = f.scope[-1][1]
        self.closure = {u.name: self.include_closure(u) for u in units}
        self.bases, self.methods, self.typedefs = {}, {}, {}
        for u in units:
            self.typedefs.update(u.typedefs)
        for u in units:
            for name, bases in u.bases.items():
                self.bases.setdefault(name, set()).update(bases)
            for name, methods in u.methods.items():
                self.methods.setdefault(name, set()).update(methods)
        self.kept = {}  # (file, tok index) -> kind
        self.kept_from = {}
        self.seen = set()
        self.queue = []
        self.parent = {}
        self.current = None

    def include_closure(self, u):
        seen, stack = {u.name}, [u.name]
        while stack:
            for h in self.by_name[stack.pop()].includes:
                if h not in seen:
                    seen.add(h)
                    stack.append(h)
        return seen

    def visible(self, d, u):
        """Can code in unit u call the function d?"""
        if d.file == u.name:
            return True
        if d.anonymous():
            return False
        if d.file in self.closure[u.name]:
            return True
        defining = self.by_name[d.file]
        return any(h in self.closure[u.name] and d.name in self.by_name[h].declared for h in defining.includes)

    # ---- the work list ----
    def push(self, item, via):
        if item in self.seen:
            return
        self.seen.add(item)
        self.parent[item] = self.current
        self.queue.append((item, via))

    def run(self):
        self.seed()
        head = 0
        while head < len(self.queue):
            item, via = self.queue[head]
            head += 1
            self.current = item
            getattr(self, "do_" + item[0])(item, via)
        self.current = None

    def keep(self, u, k, via):
        if (u.name, k) not in self.kept:
            self.kept[(u.name, k)] = via
            self.kept_from[(u.name, k)] = self.current

    def func(self, fname, bopen):
        return next(f for f in self.by_name[fname].funcs if f.bopen == bopen)

    def describe(self, item):
        kind = item[0]
        if kind == "expr":
            u = self.by_name[item[1]]
            text = " ".join(t.text if t.kind != "str" else repr(t.text)[:30] for t in u.toks[item[2]:min(item[3], item[2] + 12)])
            return f"expr {item[1]}:{u.toks[item[2]].line} {text}"
        if kind in ("ret", "param", "var", "pdefs"):
            d = self.func(item[1], item[2])
            return f"{kind} {item[1]}:{d.name}{'' if len(item) < 4 else ' ' + str(item[3])}"
        return " ".join(str(x) for x in item)

    def why(self, fname, k):
        chain, item = [], self.kept_from.get((fname, k))
        while item is not None:
            chain.append(self.describe(item))
            item = self.parent.get(item)
        return chain

    # ---- seeds ----
    def seed(self):
        for u in self.units:
            for name, idx in SINKS.items():
                for k in u.ids.get(name, ()):
                    if not u.is_op(k + 1, "(") or self.is_definition_or_declaration(u, k):
                        continue
                    args = u.split_commas(k + 2, u.match[k + 1])
                    if idx < len(args):
                        self.push(("expr", u.name, args[idx][0], args[idx][1]), name)
            for (fname, array), kind in TABLE_ARRAYS.items():
                if fname == u.name:
                    self.push(("gvar", u.name, array), kind)

    def is_definition_or_declaration(self, u, k):
        if k in u.def_name_at:
            return True
        _, first = u.qualifier_before(k)
        p = first - 1
        if u.is_op(p, ".") or u.is_op(p, "->"):
            return False
        if u.is_id(p) and u.toks[p].text not in CALL_PREFIX_KEYWORDS:
            return True
        if u.is_op(p) and u.toks[p].text in ("*", "&") and u.is_id(p - 1) and u.toks[p - 1].text not in CALL_PREFIX_KEYWORDS:
            return u.func_at[k] is None
        return False

    # ---- expressions ----
    def conditions(self, u, a, b):
        """Token indices that form the condition of a ?: inside [a, b): a condition is never text."""
        out = set()
        for q in range(a, b):
            if not u.is_op(q, "?"):
                continue
            j = q - 1
            while j >= a:
                t = u.toks[j]
                if t.kind == "op":
                    if t.text in ")]}":
                        j = u.match[j] - 1
                        continue
                    if t.text in "([{" or t.text in (",", "=", "+=", "?", ":", ";", "<<", "return"):
                        break
                elif t.kind == "id" and t.text == "return":
                    break
                j -= 1
            out.update(range(j + 1, q))
        return out

    def do_expr(self, item, via):
        _, fname, a, b = item
        u = self.by_name[fname]
        skip = self.conditions(u, a, b)
        k = a
        while k < b:
            if k in skip:
                k += 1
                continue
            t = u.toks[k]
            if t.kind == "str":
                if not self.literal_excluded(u, k, a):
                    self.keep(u, k, via)
                k += 1
                continue
            if t.kind != "id":
                k += 1
                continue
            name = t.text
            if name in ("static_cast", "const_cast", "reinterpret_cast", "dynamic_cast") and u.is_op(k + 1, "<"):
                close = u.template_close(k + 1, b)
                k = (close + 1) if close else k + 1
                continue
            if name in ("sizeof", "typeid", "alignof") and u.is_op(k + 1, "("):
                k = u.match[k + 1] + 1
                continue
            if name in KEYWORDS or u.is_op(k + 1, "::"):
                k += 1
                continue
            popen = k + 1
            if u.is_op(k + 1, "<"):  # a template call: createWidget<MyGUI::Button>(...)
                tclose = u.template_close(k + 1, b)
                if tclose and u.is_op(tclose + 1, "("):
                    popen = tclose + 1
            if u.is_op(popen, "("):
                close = u.match[popen]
                callee = u.callee_of(popen)
                qual = callee[1] if callee else []
                if name in NOT_TEXT or name in LOG_FUNCS or (qual and qual[-1] in LOG_QUALIFIERS):
                    if name == "getUserString":
                        args = u.split_commas(popen + 1, close)
                        if args and u.toks[args[0][0]].kind == "str":
                            self.push(("ukey", u.toks[args[0][0]].text), "tooltip")
                    k = close + 1
                    continue
                if name in PASSTHROUGH and callee and callee[2]:
                    k += 1
                    continue
                defs = self.resolve(u, k, callee, len(u.split_commas(popen + 1, close)))
                if defs:
                    for d in defs:
                        if not d.ret_numeric and d.name != d.cls:
                            self.push(("ret", d.file, d.bopen), via)
                    k = close + 1  # its arguments matter only through its parameters (do_ret -> do_param)
                    continue
                k += 1  # a library or engine call: its arguments are part of the text (std::string("..."), ...)
                continue
            if u.is_op(k - 1, ".") or u.is_op(k - 1, "->"):
                if not self.is_receiver(u, k):
                    self.push(("field", name, self.receiver_struct(u, k)), via)
                k += 1
                continue
            if self.is_receiver(u, k):
                k += 1
                continue
            decl = u.decl_of(k, name)
            if decl is not None and decl[0]:
                k += 1  # a number cannot carry text
                continue
            f = u.func_at[k]
            if f is not None and name in f.locals:
                self.push(("var", u.name, f.bopen, name), via)
            else:
                self.push(("gvar", u.name, name), via)
            k += 1

    def is_receiver(self, u, k):
        """toks[k] is followed by .member or ->member (after any [index]), and the member is not a pass-through."""
        j = k + 1
        while u.is_op(j, "["):
            j = u.match[j] + 1
        if not (u.is_op(j, ".") or u.is_op(j, "->")):
            return False
        m = j + 1
        return not (u.is_id(m) and u.toks[m].text in PASSTHROUGH and u.is_op(m + 1, "("))

    def expand(self, ids, depth=0):
        out = []
        for name in ids:
            if name in self.typedefs and depth < 8:
                out.extend(self.expand(self.typedefs[name], depth + 1))
            else:
                out.append(name)
        return out

    def struct_in(self, ids):
        for name in reversed(self.expand(ids)):
            if name in self.structs:
                return name
        return None

    def receiver_types(self, u, r):
        """Type ids of the object named by the id at toks[r] (a variable, or a member of another object)."""
        if u.is_op(r - 1, ".") or u.is_op(r - 1, "->"):
            o = r - 2
            while u.is_op(o, "]"):
                o = u.match[o] - 1
            if not u.is_id(o):
                return []
            outer = self.receiver_types(u, o)
            if u.toks[r].text in ("first", "second"):  # a map entry: the value type is the last struct named
                return outer
            name = self.struct_in(outer)
            for _, fields in self.structs.get(name, ()):
                for fname, _, type_ids, _ in fields:
                    if fname == u.toks[r].text:
                        return type_ids
            return []
        decl = u.decl_of(r, u.toks[r].text)
        return decl[1] if decl else []

    def receiver_struct(self, u, k):
        """The struct type of the object whose member toks[k] is (x.f, x->f, x[i].f, a.b.f, it->second.f, F().f), when it
        can be told."""
        r = k - 2
        while u.is_op(r, "]"):
            r = u.match[r] - 1
        if u.is_id(r):
            return self.struct_in(self.receiver_types(u, r))
        if u.is_op(r, ")"):
            callee = u.callee_of(u.match[r])
            if callee:
                for d in self.resolve(u, callee[3], callee, len(u.split_commas(u.match[r] + 1, r))):
                    name = self.struct_in(d.ret_ids)
                    if name:
                        return name
        return None

    def literal_excluded(self, u, k, a):
        t = u.toks[k]
        if t.wide or not_text_shape(t.text) or t.text in u.json_keys:
            return True
        if (u.is_op(k - 1) and u.toks[k - 1].text in ("==", "!=")) or (u.is_op(k + 1) and u.toks[k + 1].text in ("==", "!=")):
            return True
        j = k
        while j >= a:
            if u.is_op(j, "(") and u.match[j] > k:
                c = u.callee_of(j)
                if c and (c[0] in NOT_TEXT or c[0] in LOG_FUNCS or (c[1] and c[1][-1] in LOG_QUALIFIERS)):
                    return True
            j -= 1
        return False

    def resolve(self, u, k, callee, nargs):
        """Plugin functions the call at toks[k] can reach (empty for engine and library calls)."""
        if not callee:
            return []
        name, qual, member, _ = callee
        cands = [d for d in self.funcs_by_name.get(name, ()) if self.arity_ok(d, nargs) and self.visible(d, u)]
        caller = u.func_at[k]
        if member:
            found = [d for d in cands if d.cls]
            # A virtual call through a visible base class reaches the overrides, wherever they are defined.
            for d in self.funcs_by_name.get(name, ()):
                if d.cls and d not in found and self.arity_ok(d, nargs) and self.overrides_visible(d, u):
                    found.append(d)
            return found
        if not cands:
            return []
        caller_cls = caller.cls if caller else None
        cands = [d for d in cands if d.cls is None or d.name == d.cls or qual or (caller_cls and d.cls == caller_cls)]
        if qual:
            return [d for d in cands if qual[-1] in d.names() or (d.name == d.cls == qual[-1])]
        same = [d for d in cands if d.file == u.name]
        if same:
            return same
        spaces = (caller.namespaces() if caller else set()) | u.usings
        return [d for d in cands if (d.namespaces() & spaces) or not d.namespaces() or d.name == d.cls]

    def overrides_visible(self, d, u):
        todo, seen = list(self.bases.get(d.cls, ())), set()
        while todo:
            b = todo.pop()
            if b in seen:
                continue
            seen.add(b)
            if d.name in self.methods.get(b, ()) and any(home.name in self.closure[u.name] for home, _ in self.structs.get(b, ())):
                return True
            todo.extend(self.bases.get(b, ()))
        return False

    @staticmethod
    def arity_ok(d, nargs):
        required = sum(1 for p in d.params if not p.default)
        return required <= nargs <= len(d.params)

    # ---- functions ----
    def do_ret(self, item, via):
        _, fname, bopen = item
        d = self.func(fname, bopen)
        u = self.by_name[fname]
        for k in u.ids.get("return", ()):
            if d.bopen < k < d.bclose:
                e = u.expr_end(k + 1, d.bclose)
                if e > k + 1:
                    self.push(("expr", fname, k + 1, e), via)
        # An accessor returning a reference is also written through: Name(offer, i) = text;
        for other in self.units:
            if not self.visible(d, other):
                continue
            for k in other.ids.get(d.name, ()):
                if not other.is_op(k + 1, "(") or self.is_definition_or_declaration(other, k):
                    continue
                close = other.match[k + 1]
                if other.is_op(close + 1) and other.toks[close + 1].text in ("=", "+=") and \
                        d in self.resolve(other, k, other.callee_of(k + 1), len(other.split_commas(k + 2, close))):
                    self.push(("expr", other.name, close + 2, other.expr_end(close + 2, len(other.toks))), via)

    def do_param(self, item, via):
        _, fname, bopen, idx = item
        d = self.func(fname, bopen)
        if d.params[idx].numeric:
            return
        label = d.name if d.name in NAMED_WRAPPERS else via
        for u in self.units:
            if not self.visible(d, u):
                continue
            for k in u.ids.get(d.name, ()):
                if not u.is_op(k + 1, "(") or self.is_definition_or_declaration(u, k):
                    continue
                close = u.match[k + 1]
                args = u.split_commas(k + 2, close)
                if idx < len(args) and d in self.resolve(u, k, u.callee_of(k + 1), len(args)):
                    self.push(("expr", u.name, args[idx][0], args[idx][1]), label)

    # ---- variables ----
    def do_var(self, item, via):
        _, fname, bopen, name = item
        d = self.func(fname, bopen)
        for i, p in enumerate(d.params):
            if p.name == name:
                self.push(("param", fname, bopen, i), via)
        self.defs_in(self.by_name[fname], d.popen, d.bclose, name, via, d)

    def do_pdefs(self, item, via):
        _, fname, bopen, name = item
        d = self.func(fname, bopen)
        self.defs_in(self.by_name[fname], d.popen, d.bclose, name, via, d)

    def do_gvar(self, item, via):
        _, fname, name = item
        u = self.by_name[fname]
        if self.defs_in(u, 0, len(u.toks), name, via, None):
            return
        for h in sorted(self.closure[fname] - {fname}):  # an extern or static member from an included header
            self.defs_in(self.by_name[h], 0, len(self.by_name[h].toks), name, via, None)

    def defs_in(self, u, a, b, name, via, local_func):
        found = False
        for k in u.ids.get(name, ()):
            if not a <= k < b:
                continue
            f = u.func_at[k]
            if local_func is None and f is not None and name in f.locals:
                continue  # a local of some function, not this global
            if u.is_op(k - 1, ".") or u.is_op(k - 1, "->"):
                continue
            found |= self.def_at(u, k, via)
        return found

    def def_at(self, u, k, via):
        """If toks[k] (a variable or member) is written here, taint what is written; True when it is a write."""
        if k in u.def_name_at:
            return False
        if u.is_declared_at(k) and u.decl_type(k)[0]:
            return True  # declared as a number
        j = k + 1
        while u.is_op(j, "["):
            j = u.match[j] + 1
        nx = u.toks[j] if j < len(u.toks) else None
        if nx is None:
            return False
        if nx.kind == "op" and nx.text in ("=", "+="):
            self.push(("expr", u.name, j + 1, u.expr_end(j + 1, len(u.toks))), via)
            return True
        if nx.kind == "op" and nx.text in ("(", "{") and u.is_declared_at(k):
            self.push(("expr", u.name, j + 1, u.match[j]), via)
            return True
        if nx.kind == "op" and nx.text in (".", "->") and u.is_id(j + 1) and u.toks[j + 1].text in APPENDERS \
                and u.is_op(j + 2, "("):
            self.push(("expr", u.name, j + 3, u.match[j + 2]), via)
            return True
        if nx.kind == "op" and nx.text == "<<" and self.statement_start(u, k):
            self.push(("expr", u.name, j + 1, u.expr_end(j + 1, len(u.toks))), via)
            return True
        p = self.enclosing_paren(u, k)
        if nx.kind == "op" and nx.text == ":" and u.is_declared_at(k) and p is not None:  # for (auto& x : container)
            self.push(("expr", u.name, j + 1, u.match[p]), via)
            return True
        if p is not None:  # written as an argument: a C writer's buffer, or a plugin function's out-parameter
            callee = u.callee_of(p)
            args = u.split_commas(p + 1, u.match[p])
            pos = next((i for i, (s, e) in enumerate(args) if s <= k < e), None)
            if callee and pos is not None:
                whole = self.lvalue_arg(u, args[pos], k)
                if callee[0] in WRITERS and pos == 0 and whole:
                    for s2, e2 in args[1:]:
                        self.push(("expr", u.name, s2, e2), via)
                    return True
                if whole and callee[0] not in NOT_TEXT:
                    for d in self.resolve(u, callee[3], callee, len(args)):
                        if pos < len(d.params) and d.params[pos].out and d.params[pos].name:
                            self.push(("pdefs", d.file, d.bopen, d.params[pos].name), via)
                            return True
        return False

    @staticmethod
    def lvalue_arg(u, arg, k):
        """The argument [s, e) is the object toks[k] names: x, &x, *x, x[i], a.b.x, p->x, a[i].x (optionally indexed)."""
        s, e = arg
        n = s + 1 if u.is_op(s) and u.toks[s].text in ("&", "*") else s
        while True:
            if not u.is_id(n):
                return False
            end = n + 1
            while u.is_op(end, "["):
                end = u.match[end] + 1
            if n == k:
                return end == e
            if not (u.is_op(end, ".") or u.is_op(end, "->")):
                return False
            n = end + 1

    @staticmethod
    def statement_start(u, k):
        """toks[k] begins a statement: after ; { } else do, or after the ) of an if / for / while condition."""
        p = k - 1
        if u.is_op(p) and u.toks[p].text in (";", "{", "}"):
            return True
        if u.is_id(p) and u.toks[p].text in ("else", "do"):
            return True
        if u.is_op(p, ")"):
            return u.is_id(u.match[p] - 1) and u.toks[u.match[p] - 1].text in ("if", "for", "while")
        return False

    def enclosing_paren(self, u, k):
        j = k - 1
        while j >= 0:
            t = u.toks[j]
            if t.kind == "op":
                if t.text in ")]}":
                    j = u.match[j] - 1
                    continue
                if t.text == "(":
                    return j
                if t.text in ("[", "{", ";"):
                    return None
            j -= 1
        return None

    # ---- fields and tables ----
    def do_field(self, item, via):
        _, name, struct = item
        owners = [s for s in self.structs if any(f[0] == name for _, fl in self.structs[s] for f in fl)]
        if struct in owners:
            owners = [struct]
        info = [(s, u, i, f) for s in owners for u, fl in self.structs[s] for i, f in enumerate(fl) if f[0] == name]
        if info and all(f[1] for _, _, _, f in info):
            return  # a number in every struct that has it
        homes = {u.name for _, u, _, _ in info}
        for u in self.units:
            if homes and not (homes & self.closure[u.name]):
                continue
            for k in u.ids.get(name, ()):
                if u.is_op(k - 1, ".") or u.is_op(k - 1, "->"):
                    rs = self.receiver_struct(u, k)
                    if struct is None or rs is None or rs == struct:
                        self.def_at(u, k, via)
            for f in u.funcs:
                if f.cls in owners:
                    for member, s, e in f.initlist:
                        if member == name:
                            self.push(("expr", u.name, s, e), via)
                    if name not in f.locals:
                        self.defs_in(u, f.bopen, f.bclose, name, via, f)
        for s, u, i, f in info:
            if f[3]:
                self.push(("expr", u.name, f[3][0], f[3][1]), via)
        for s, _, i, _ in info:
            for u in self.units:
                self.struct_tables(u, s, i, via)

    def struct_tables(self, u, struct, index, via):
        for k in u.ids.get(struct, ()):
            if not u.is_id(k + 1) or u.func_at[k] is not None and u.is_op(k - 1, "("):
                continue
            j = k + 2
            while u.is_op(j, "["):
                j = u.match[j] + 1
            if u.is_op(j, "="):
                j += 1
            if u.is_op(j, "{"):
                self.table_column(u, j, index, via)

    def table_column(self, u, brace, index, via):
        parts = u.split_commas(brace + 1, u.match[brace])
        if parts and all(u.is_op(s, "{") and u.match[s] == e - 1 for s, e in parts):
            for s, _ in parts:
                self.table_column(u, s, index, via)
            return
        if index < len(parts):
            self.push(("expr", u.name, parts[index][0], parts[index][1]), via)

    # ---- user strings (tooltips) ----
    def do_ukey(self, item, via):
        _, key = item
        for u in self.units:
            for k in u.ids.get("setUserString", ()):
                if u.is_op(k + 1, "("):
                    args = u.split_commas(k + 2, u.match[k + 1])
                    if len(args) == 2 and u.toks[args[0][0]].kind == "str" and u.toks[args[0][0]].text == key:
                        self.push(("expr", u.name, args[1][0], args[1][1]), via)


def not_text_shape(text):
    """Literals whose shape says they are not prose: record stringIDs, file names, dotted ids, skins and widget names."""
    if STRING_ID_RE.match(text):
        return True
    if re.fullmatch(r"[\w./\\-]*\.(json|log|txt|dll|mod|base|tmp|backup|layout|png|dds)", text):
        return True
    if re.fullmatch(r"[a-z0-9_]+(\.[a-z0-9_]+)+", text):
        return True
    if re.match(r"(Kenshi_|PG_|pg_)[\w%]*$", text):
        return True
    return False


def table_rule(units, kept):
    """Every literal of the table files (TABLE_FILES) that no exclusion rule marks as not text."""
    for u in units:
        kind = TABLE_FILES.get(u.name)
        if not kind:
            continue
        for k, t in enumerate(u.toks):
            if t.kind == "str" and (u.name, k) not in kept and not excluded_reason(u, k) and not in_writer(u, k):
                kept[(u.name, k)] = kind


def in_writer(u, k):
    """The literal is an argument of an sprintf-style writer; whether that buffer is shown is the taint analysis's call."""
    j = k
    while j >= 0 and not (u.is_op(j, ";") or u.is_op(j, "{") or u.is_op(j, "}")):
        if u.is_op(j, "(") and u.match[j] > k:
            c = u.callee_of(j)
            if c and c[0] in WRITERS:
                return True
        j -= 1
    return False


def excluded_reason(u, k):
    """Why a literal is not player text, or None when no rule explains it (used by the table rule and --rest)."""
    t = u.toks[k]
    if t.wide:
        return "wide string (file path)"
    if STRING_ID_RE.match(t.text):
        return "record stringID"
    if not_text_shape(t.text):
        return "file name, dotted id, skin or widget name"
    if t.text in u.json_keys:
        return "JSON key"
    if (u.is_op(k - 1) and u.toks[k - 1].text in ("==", "!=")) or (u.is_op(k + 1) and u.toks[k + 1].text in ("==", "!=")):
        return "compared"
    j = k
    while j >= 0:
        if u.is_op(j, ";") or (u.is_op(j, "{") and u.func_at[j] is not None) or u.is_op(j, "}"):
            break
        if u.is_op(j, "(") and u.match[j] > k:
            c = u.callee_of(j)
            if c:
                name, qual = c[0], c[1]
                if (qual and qual[-1] in LOG_QUALIFIERS) or name in LOG_FUNCS:
                    return "log (" + "::".join(qual + [name]) + ")"
                if name in ("createWidget", "createWidgetReal", "createWidgetT"):
                    return "widget skin, layer or name"
                if name in ("setUserString", "getUserString"):
                    if k < u.split_commas(j + 1, u.match[j])[0][1]:
                        return "user-string key"
                    return None
                if name in ("Field", "Member", "HasMember", "FindMember", "Key", "Index"):
                    return "JSON key"
                if name in NOT_TEXT:
                    return f"argument of {name}"
        j -= 1
    return None


def build():
    units = [Unit(p) for p in sorted(SRC.rglob("*")) if p.is_file() and p.suffix in (".h", ".cpp")]
    an = Analysis(units)
    an.run()
    table_rule(units, an.kept)
    by_unit = {u.name: u for u in units}
    entries = {}
    for (fname, k), kind in an.kept.items():
        t = by_unit[fname].toks[k]
        if t.text == "":
            continue  # an empty caption is not text
        key = (fname, t.line, t.text)
        if key not in entries or kind < entries[key]["kind"]:
            entries[key] = {"text": t.text, "file": fname, "line": t.line, "kind": kind}
    return units, an, [entries[k] for k in sorted(entries)]


def main():
    args = sys.argv[1:]
    units, an, catalog = build()
    text = json.dumps(catalog, ensure_ascii=False, indent=1) + "\n"
    if "--check" in args:
        current = OUT.read_text(encoding="utf-8") if OUT.is_file() else ""
        if current != text:
            sys.exit("fidelity/strings.json is out of date: run python3 tools/extract_strings.py")
        print(f"fidelity/strings.json is up to date ({len(catalog)} strings)")
        return
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(text, encoding="utf-8", newline="\n")
    total = sum(1 for u in units for t in u.toks if t.kind == "str")
    print(f"wrote {OUT.relative_to(ROOT)}: {len(catalog)} player-visible strings of {total} string literals in "
          f"{len(units)} files")
    kinds, files = {}, {}
    for e in catalog:
        kinds[e["kind"]] = kinds.get(e["kind"], 0) + 1
        files[e["file"]] = files.get(e["file"], 0) + 1
    print("by kind:")
    for k, n in sorted(kinds.items(), key=lambda x: (-x[1], x[0])):
        print(f"  {k:20} {n}")
    print("by file:")
    for k, n in sorted(files.items()):
        print(f"  {k:36} {n}")
    wrappers = sorted({"::".join(an.func(i[1], i[2]).names() + [an.func(i[1], i[2]).name]).replace("::" * 2, "::")
                       for i in an.seen if i[0] == "param"})
    print(f"functions whose text parameter reaches a sink ({len(wrappers)}): " + ", ".join(wrappers))
    by_unit = {u.name: u for u in units}
    kept_keys = {(e["file"], e["line"], e["text"]) for e in catalog}
    left = [(u, k) for u in units for k, t in enumerate(u.toks)
            if t.kind == "str" and (u.name, t.line, t.text) not in kept_keys and t.text]
    if "--sample" in args:
        n = int(args[args.index("--sample") + 1])
        seed = int(args[args.index("--seed") + 1]) if "--seed" in args else 0
        rng = random.Random(seed)
        print(f"\n--- {n} kept (seed {seed})")
        for e in rng.sample(catalog, min(n, len(catalog))):
            print(f"{e['file']}:{e['line']} [{e['kind']}] {e['text']!r}")
        print(f"\n--- {n} left out (seed {seed})")
        for u, k in rng.sample(left, min(n, len(left))):
            print(f"{u.name}:{u.toks[k].line} [{excluded_reason(u, k) or 'not reached from a sink'}] {u.toks[k].text!r}")
    for i, a in enumerate(args):
        if a == "--why":
            fname, line = args[i + 1].rsplit(":", 1)
            u = by_unit[fname]
            for k, t in enumerate(u.toks):
                if t.kind == "str" and t.line == int(line) and (fname, k) in an.kept:
                    print(f"\n--- why {t.text!r}")
                    for step in an.why(fname, k):
                        print("  <- " + step)
    if "--rest" in args:
        print("\n--- left out, no exclusion rule explains it")
        for u, k in left:
            if not excluded_reason(u, k):
                print(f"{u.name}:{u.toks[k].line} {u.toks[k].text!r}")


if __name__ == "__main__":
    main()
