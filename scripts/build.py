#!/usr/bin/env python3
"""Build the all-in-one installer and the website from the tools/ folder.

Usage:  python scripts/build.py

Writes to dist/ (not committed; GitHub Actions publishes it to GitHub Pages):
  dist/install.ps1                 for:  irm <site>/install.ps1 | iex
  dist/WindowsUnlockedToolkit.cmd  the same installer as a double-clickable file
  dist/index.html                  the website (from site/index.template.html,
                                   site/showcase.json and site/assets/)
  dist/manifest.ini                each tool's newest version; the tray app reads it
                                   to update automatically, and the setup app embeds it

The setup app (WindowsUnlockedToolkitSetup.exe) is built on Windows by
scripts/build-setup.ps1 from dist/install.ps1 and dist/manifest.ini.

Each tool lives in tools/<id>/ with a tool.json (see CLAUDE.md for the fields).
Every file in the tool's folder except tool.json is embedded in the installer.
"""

import base64
import datetime
import html
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
TOOLS = ROOT / "tools"
DIST = ROOT / "dist"
REPO_URL = "https://github.com/2wanderingcrows/windows-unlocked-toolkit"
SITE_URL = "https://2wanderingcrows.github.io/windows-unlocked-toolkit"
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
        f"        Admin = {'$true' if t['admin'] else '$false'}; Detect = {ps_quote(t.get('detect', ''))}\n"
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


def load_showcase():
    path = ROOT / "site" / "showcase.json"
    if not path.is_file():
        return {}, []
    data = json.loads(path.read_text(encoding="utf-8"))
    return data.get("tools", {}), data.get("upcoming", [])


def rich(text):
    """Escape text, then turn **bold** into <b>bold</b>."""
    parts = html.escape(text).split("**")
    return "".join(f"<b>{p}</b>" if i % 2 else p for i, p in enumerate(parts))


def tool_card(t, show, index):
    e = html.escape
    show = show or {}
    src = f"{REPO_URL}/tree/main/tools/{t['id']}"
    accent = e(show.get("accent", ""))
    style = f" style=\"--tool:{accent}\"" if accent else ""
    image = show.get("image")
    thumb = (f"<img src=\"{e(image)}\" alt=\"\" loading=\"lazy\">" if image
             else f"<span>{e(t['name'][:1])}</span>")
    bullets = "\n".join(f"          <li>{rich(b)}</li>" for b in show.get("bullets", []))
    checks = f"        <ul class=\"checks\">\n{bullets}\n        </ul>\n" if bullets else ""
    admin = "Needs admin" if t.get("admin") else "No admin needed"
    figure = ""
    if image:
        badge = "<span class=\"mock\">Illustration</span>" if show.get("mockup") else ""
        figure = (f"      <figure class=\"shot\">{badge}<img src=\"{e(image)}\" alt=\"{e(show.get('alt', ''))}\""
                  " loading=\"lazy\" width=\"1200\" height=\"640\"></figure>\n")
    return (
        f"    <details class=\"tool\" id=\"{e(t['id'])}\"{style}>\n"
        "      <summary>\n"
        f"        <span class=\"thumb\">{thumb}</span>\n"
        f"        <span class=\"title\"><b>{e(t['name'])}</b> <span class=\"ver\">v{e(t['version'])}</span>"
        f"<span class=\"tagline\">{e(show.get('tagline', t['summary']))}</span></span>\n"
        "        <span class=\"chev\" aria-hidden=\"true\"></span>\n"
        "      </summary>\n"
        "      <div class=\"body\">\n"
        "      <div class=\"copy\">\n"
        f"        <p class=\"problem\">{rich(show.get('problem', t['summary']))}</p>\n"
        f"{checks}"
        f"        <div class=\"meta\"><span class=\"pill\">{admin}</span><a class=\"more\" href=\"{src}\">Details and source &rarr;</a></div>\n"
        "      </div>\n"
        f"{figure}"
        "      </div>\n"
        "    </details>"
    )


def build_site(tools, build):
    template = (ROOT / "site" / "index.template.html").read_text(encoding="utf-8")
    showcase, upcoming = load_showcase()
    cards = "\n".join(tool_card(t, showcase.get(t["id"]), i) for i, t in enumerate(tools))
    soon = "\n".join(
        f"      <li><b>{html.escape(u['name'])}</b><span>{html.escape(u.get('note', ''))}</span></li>"
        for u in upcoming
    )
    return (template.replace("{{TOOLS}}", cards)
                    .replace("{{UPCOMING}}", soon)
                    .replace("{{TOOL_COUNT}}", str(len(tools)))
                    .replace("{{BUILD}}", html.escape(build))
                    .replace("{{REPO_URL}}", REPO_URL)
                    .replace("{{SITE_URL}}", SITE_URL))


def build_manifest(tools, build):
    """Flat key=value lines (no JSON parser needed in the C# 5 apps)."""
    def clean(text):
        if not text.isascii() or "\n" in text:
            fail(f"manifest text must be one line of ASCII: {text!r}")
        return text
    lines = [
        "# Windows Unlocked Toolkit: the newest version of each tool.",
        "# The tray app downloads this to see if there are updates. Generated by scripts/build.py.",
        f"build={clean(build)}",
        f"installer={SITE_URL}/install.ps1",
        "tools=" + ",".join(t["id"] for t in tools),
    ]
    for t in tools:
        for key in ("name", "version", "summary", "detect"):
            lines.append(f"{t['id']}.{key}={clean(str(t.get(key, '')))}")
        lines.append(f"{t['id']}.admin={'true' if t['admin'] else 'false'}")
    return "\r\n".join(lines) + "\r\n"


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
    (DIST / "manifest.ini").write_bytes(build_manifest(tools, build).encode("ascii"))
    (DIST / "index.html").write_text(build_site(tools, build), encoding="utf-8")
    assets = ROOT / "site" / "assets"
    if assets.is_dir():
        shutil.copytree(assets, DIST / "assets", dirs_exist_ok=True)
    print(f"Built {len(tools)} tool(s): {', '.join(t['name'] for t in tools)}")
    print(f"Build {build} -> {DIST}")


if __name__ == "__main__":
    main()
