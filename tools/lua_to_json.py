#!/usr/bin/env python3
"""Convert Path of Building (PoE2) Lua data files into JSON.

Handles the two shapes used in src/Data:
  1. `return { ... }`            -> one table
  2. `return function(itemBases) itemBases["X"] = { ... } ... end`  (Bases/*.lua)
Only the Lua-table subset that PoB's generator emits is supported
(strings, numbers, booleans, nested tables, [key]= / name= / positional entries).
"""
import json
import re
import sys
from pathlib import Path


class LuaParser:
    def __init__(self, text: str):
        self.s = text
        self.i = 0
        self.n = len(text)

    # ---- low level -------------------------------------------------------
    def skip_ws(self):
        while self.i < self.n:
            c = self.s[self.i]
            if c in " \t\r\n":
                self.i += 1
            elif self.s.startswith("--", self.i):
                # long comment --[[ ... ]] or line comment
                if self.s.startswith("--[[", self.i):
                    end = self.s.find("]]", self.i + 4)
                    self.i = self.n if end < 0 else end + 2
                else:
                    end = self.s.find("\n", self.i)
                    self.i = self.n if end < 0 else end + 1
            else:
                break

    def peek(self):
        self.skip_ws()
        return self.s[self.i] if self.i < self.n else ""

    def expect(self, ch):
        self.skip_ws()
        if self.i >= self.n or self.s[self.i] != ch:
            raise SyntaxError(f"expected {ch!r} at {self.i}: {self.s[self.i:self.i+60]!r}")
        self.i += 1

    # ---- values ----------------------------------------------------------
    def parse_value(self):
        self.skip_ws()
        c = self.s[self.i]
        if c == "{":
            return self.parse_table()
        if c == '"' or c == "'":
            return self.parse_string(c)
        if self.s.startswith("[[", self.i):
            end = self.s.find("]]", self.i + 2)
            v = self.s[self.i + 2:end]
            self.i = end + 2
            return v
        m = re.compile(r"-?(?:0x[0-9a-fA-F]+|\d+\.?\d*(?:[eE][-+]?\d+)?|\.\d+)").match(self.s, self.i)
        if m and m.group(0) not in ("-",):
            self.i = m.end()
            t = m.group(0)
            if t.startswith(("0x", "-0x")):
                return int(t, 16)
            if any(x in t for x in ".eE"):
                return float(t)
            return int(t)
        m = re.compile(r"[A-Za-z_][A-Za-z0-9_]*").match(self.s, self.i)
        if m:
            word = m.group(0)
            self.i = m.end()
            if word == "true":
                return True
            if word == "false":
                return False
            if word == "nil":
                return None
            # bare identifier (e.g. a reference) - keep as string marker
            return {"__ident__": word}
        raise SyntaxError(f"unexpected char {c!r} at {self.i}: {self.s[self.i:self.i+60]!r}")

    def parse_string(self, q):
        assert self.s[self.i] == q
        self.i += 1
        out = []
        while self.i < self.n:
            c = self.s[self.i]
            if c == "\\":
                nxt = self.s[self.i + 1]
                mapping = {"n": "\n", "t": "\t", "r": "\r", "\\": "\\", '"': '"', "'": "'", "\n": "\n"}
                if nxt in mapping:
                    out.append(mapping[nxt])
                    self.i += 2
                elif nxt.isdigit():
                    m = re.compile(r"\d{1,3}").match(self.s, self.i + 1)
                    out.append(chr(int(m.group(0))))
                    self.i = m.end()
                else:
                    out.append(nxt)
                    self.i += 2
            elif c == q:
                self.i += 1
                return "".join(out)
            else:
                out.append(c)
                self.i += 1
        raise SyntaxError("unterminated string")

    def parse_table(self):
        self.expect("{")
        result = {}
        positional = []
        while True:
            self.skip_ws()
            if self.s[self.i] == "}":
                self.i += 1
                break
            # entry
            if self.s[self.i] == "[":
                self.i += 1
                key = self.parse_value()
                self.expect("]")
                self.expect("=")
                val = self.parse_value()
                result[str(key) if not isinstance(key, str) else key] = val
            else:
                m = re.compile(r"([A-Za-z_][A-Za-z0-9_]*)\s*=(?!=)").match(self.s, self.i)
                if m:
                    key = m.group(1)
                    self.i = m.end()
                    val = self.parse_value()
                    result[key] = val
                else:
                    positional.append(self.parse_value())
            self.skip_ws()
            if self.s[self.i] in ",;":
                self.i += 1
        if positional and not result:
            return positional
        if positional:
            result["__list__"] = positional
        return result


def parse_return_table(text: str):
    p = LuaParser(text)
    p.skip_ws()
    idx = text.find("return", p.i)
    p.i = idx + len("return")
    return p.parse_value()


def parse_bases(text: str):
    out = {}
    for m in re.finditer(r'itemBases\["((?:[^"\\]|\\.)*)"\]\s*=\s*', text):
        name = m.group(1).encode().decode("unicode_escape") if "\\" in m.group(1) else m.group(1)
        p = LuaParser(text)
        p.i = m.end()
        out[name] = p.parse_table()
    return out


def convert(path: Path):
    text = path.read_text(encoding="utf-8")
    if "itemBases[" in text:
        return parse_bases(text)
    return parse_return_table(text)


if __name__ == "__main__":
    src = Path(sys.argv[1])
    dst = Path(sys.argv[2])
    dst.mkdir(parents=True, exist_ok=True)
    files = [src] if src.is_file() else sorted(src.rglob("*.lua"))
    for f in files:
        try:
            data = convert(f)
        except Exception as e:  # noqa
            print(f"FAIL {f}: {e}")
            continue
        rel = f.relative_to(src if src.is_dir() else src.parent)
        out = dst / rel.with_suffix(".json")
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(json.dumps(data, ensure_ascii=False, indent=None), encoding="utf-8")
        cnt = len(data) if hasattr(data, "__len__") else "?"
        print(f"ok   {rel} -> {cnt} entries")
