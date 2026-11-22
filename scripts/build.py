#!/usr/bin/env python3
"""Build the all-in-one installer and the website from the tools/ folder.

Usage:  python scripts/build.py

Writes to dist/ (not committed; GitHub Actions publishes it to GitHub Pages):
  dist/install.ps1                 for:  irm <site>/install.ps1 | iex
  dist/WindowsUnlockedToolkit.cmd  the same installer as a double-clickable file
  dist/index.html                  the website

Each tool lives in tools/<id>/ with a tool.json (see CLAUDE.md for the fields).
Every file in the tool's folder except tool.json is embedded in the installer.
"""

import base64
import datetime
import html
import json
import os
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
TOOLS = ROOT / "tools"
DIST = ROOT / "dist"
REPO_URL = "https://github.com/maxsilbert805/windows-unlocked-toolkit"
SITE_URL = "https://maxsilbert805.github.io/windows-unlocked-toolkit"
REQUIRED = ["id", "name", "version", "summary", "install", "uninstall"]


def fail(msg):
    sys.exit(f"build failed: {msg}")


def ps_quote(text):
    if not text.isascii():
        fail(f"non-ASCII text isn't allowed in tool.json fields: {text!r}")
    return "'" + text.replace("'", "''") + "'"


def build_id():
    sha = os.environ.get("GITHUB_SHA", "")
    if not sha:
        try:
            sha = subprocess.run(["git", "rev-parse", "HEAD"], cwd=ROOT,
                                 capture_output=True, text=True).stdout.strip()
        except OSError:
            sha = ""
    date = datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%d")
    return f"{date} ({sha[:7]})" if sha else date


def load_tools():
    tools = []
    for meta_path in sorted(TOOLS.glob("*/tool.json")):
        folder = meta_path.parent
        meta = json.loads(meta_path.read_text(encoding="utf-8"))
        for key in REQUIRED:
            if not meta.get(key):
                fail(f"{meta_path} is missing '{key}'")
        if meta["id"] != folder.name:
            fail(f"{meta_path}: id '{meta['id']}' must match its folder name '{folder.name}'")
        for key in ("install", "uninstall"):
            if not (folder / meta[key]).is_file():
                fail(f"{meta_path}: {key} script '{meta[key]}' doesn't exist")
        files = sorted(p for p in folder.rglob("*") if p.is_file() and p.name != "tool.json")
        meta["files"] = [(p.relative_to(folder).as_posix().replace("/", "\\"), p.read_bytes()) for p in files]
        meta.setdefault("order", 100)
        meta.setdefault("admin", False)
        tools.append(meta)
    if not tools:
        fail("no tools found in tools/")
    tools.sort(key=lambda t: (t["order"], t["name"].lower()))
    return tools


def tool_block(t):
    files = ",\n".join(
        f"            @{{ Path = {ps_quote(path)}; Data = '{base64.b64encode(data).decode()}' }}"
        for path, data in t["files"]
    )
    return (
        "    @{\n"
        f"        Id = {ps_quote(t['id'])}; Name = {ps_quote(t['name'])}; Version = {ps_quote(t['version'])}\n"
        f"        Summary = {ps_quote(t['summary'])}\n"
        f"        Install = {ps_quote(t['install'])}; Uninstall = {ps_quote(t['uninstall'])}\n"
        f"        Admin = {'$true' if t['admin'] else '$false'}\n"
        "        Files = @(\n"
        f"{files}\n"
        "        )\n"
        "    }"
    )


CMD_HEADER = (
    "@echo off\r\n"
    "title Windows Unlocked Toolkit\r\n"
    "rem Everything below the marker line is the PowerShell installer. This header\r\n"
    "rem reads this same file and runs that part. \"Bypass\" applies to this run only.\r\n"
    "set \"WUT_SELF=%~f0\"\r\n"
    "powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "
    "\"$t=[IO.File]::ReadAllText($env:WUT_SELF); $m='#'+'#WUT-POWERSHELL-BELOW#'+'#'; "
    "Invoke-Expression $t.Substring($t.IndexOf($m))\"\r\n"
    "echo.\r\n"
    "pause\r\n"
    "exit /b\r\n"
)


def build_site(tools, build):
    template = (ROOT / "site" / "index.template.html").read_text(encoding="utf-8")
    cards = "\n".join(
        "      <li class=\"tool\">\n"
        f"        <h3>{html.escape(t['name'])} <span class=\"ver\">v{html.escape(t['version'])}</span></h3>\n"
        f"        <p>{html.escape(t['summary'])}</p>\n"
        f"        <a href=\"{REPO_URL}/tree/main/tools/{t['id']}\">Source and details</a>\n"
        "      </li>"
        for t in tools
    )
    return (template.replace("{{TOOLS}}", cards)
                    .replace("{{BUILD}}", html.escape(build))
                    .replace("{{REPO_URL}}", REPO_URL)
                    .replace("{{SITE_URL}}", SITE_URL))


def main():
    tools = load_tools()
    build = build_id()
    template = (ROOT / "scripts" / "installer-template.ps1").read_text(encoding="utf-8")
    script = (template.replace("__TOOLS__", ",\n".join(tool_block(t) for t in tools))
                      .replace("__BUILD__", build)
                      .replace("__REPO_URL__", REPO_URL))
    if not script.isascii():
        fail("the installer must be plain ASCII")
    script = script.replace("\r\n", "\n").replace("\n", "\r\n")

    DIST.mkdir(exist_ok=True)
    (DIST / "install.ps1").write_bytes(script.encode("ascii"))
    (DIST / "WindowsUnlockedToolkit.cmd").write_bytes((CMD_HEADER + script).encode("ascii"))
    (DIST / "index.html").write_text(build_site(tools, build), encoding="utf-8")
    print(f"Built {len(tools)} tool(s): {', '.join(t['name'] for t in tools)}")
    print(f"Build {build} -> {DIST}")


if __name__ == "__main__":
    main()
