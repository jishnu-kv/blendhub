#!/usr/bin/env python3
"""
Blender Version Scraper
Scrapes the Blender release site and generates blender_versions.json
Usage: python scrape_blender_versions.py [--limit N] [--output path/to/blender_versions.json]
"""

import re
import json
import argparse
import urllib.request
import urllib.error
from datetime import datetime
from pathlib import Path

BASE_URL = "https://download.blender.org/release/"
DEFAULT_OUTPUT = Path(__file__).parent / "BlendHub" / "blender_versions.json"
WINDOWS_EXTS = {".exe", ".msi", ".msix", ".zip"}


def fetch(url: str) -> str:
    req = urllib.request.Request(url, headers={"User-Agent": "BlendHub/1.0"})
    with urllib.request.urlopen(req, timeout=30) as resp:
        return resp.read().decode("utf-8", errors="replace")


def parse_directory_listing(html: str, base_url: str) -> list[dict]:
    """Parse Apache-style directory listing into a list of entries."""
    results = []
    pattern = re.compile(
        r'<a href="([^"]+)">[^<]+</a>\s+(\d{2}-\w{3}-\d{4}\s+\d{2}:\d{2})\s+([\d-]+|-)'
    )
    for m in pattern.finditer(html):
        href = m.group(1)
        if href in ("../", "/"):
            continue
        name = href.rstrip("/")
        date = m.group(2).strip()
        size_str = m.group(3)
        size = int(size_str) if size_str.isdigit() else 0
        results.append({"name": name, "url": base_url + href, "date": date, "size_bytes": size})
    return results


def is_windows_installer(filename: str) -> bool:
    lower = filename.lower()
    has_keyword = bool(re.search(r"windows|_win", lower))
    ext = Path(lower).suffix
    return has_keyword and ext in WINDOWS_EXTS


def parse_version_tuple(version_str: str) -> tuple:
    """Convert '4.2.1' or '2.79b' to a sortable tuple."""
    parts = re.split(r"[.\-]", version_str)
    result = []
    for part in parts:
        m = re.match(r"(\d+)(.*)", part)
        if m:
            result.append((int(m.group(1)), m.group(2)))
        else:
            result.append((0, part))
    return tuple(result)


def get_short_version(version_str: str) -> str:
    """Get 'X.Y' from any version string like '4.2.1', '4.2', '2.79b'."""
    m = re.match(r"(\d+)\.(\d+)", version_str)
    if m:
        return f"{m.group(1)}.{m.group(2)}"
    return version_str


def scrape(limit: int = 15) -> list[dict]:
    print(f"Fetching version list from {BASE_URL}...")
    html = fetch(BASE_URL)
    all_dirs = parse_directory_listing(html, BASE_URL)

    version_dirs = [
        d for d in all_dirs
        if d["name"].lower().startswith("blender")
    ]

    # Sort newest first
    version_dirs.sort(
        key=lambda d: parse_version_tuple(d["name"][7:] if d["name"].lower().startswith("blender") else d["name"]),
        reverse=True
    )

    print(f"Found {len(version_dirs)} version directories. Scraping top {limit}...")

    versions = []
    for entry in version_dirs[:limit]:
        dirname = entry["name"]
        version_str = dirname[7:] if dirname.lower().startswith("blender") else dirname

        print(f"  Scraping {dirname}...", end=" ", flush=True)
        try:
            sub_html = fetch(entry["url"])
            files = parse_directory_listing(sub_html, entry["url"])
            win_files = [f for f in files if is_windows_installer(f["name"])]

            if win_files:
                installers = [
                    {
                        "filename": f["name"],
                        "url": f["url"],
                        "release_date": f["date"],
                        "size_bytes": f["size_bytes"],
                    }
                    for f in win_files
                ]
                versions.append({
                    "version": version_str,
                    "windows_installers": installers,
                })
                print(f"OK ({len(installers)} installers)")
            else:
                print("no Windows installers, skipped")
        except Exception as e:
            print(f"FAILED: {e}")

    return versions


def main():
    parser = argparse.ArgumentParser(description="Scrape Blender releases to JSON")
    parser.add_argument("--limit", type=int, default=15, help="Number of newest versions to scrape (default: 15)")
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT, help="Output JSON path")
    parser.add_argument("--all", action="store_true", help="Scrape all available versions (slow)")
    args = parser.parse_args()

    limit = 9999 if args.all else args.limit

    versions = scrape(limit)

    output: Path = args.output
    output.parent.mkdir(parents=True, exist_ok=True)

    with open(output, "w", encoding="utf-8") as f:
        json.dump(versions, f, indent=2, ensure_ascii=False)

    print(f"\nSaved {len(versions)} versions to {output}")


if __name__ == "__main__":
    main()
