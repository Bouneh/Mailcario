"""Render the repository's GitHub star history as a self-hosted SVG."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
from html import escape
import json
import os
from pathlib import Path
from urllib.request import Request, urlopen


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "docs" / "assets" / "star-history.svg"


def fetch_stars(repository: str, token: str) -> list[datetime]:
    dates: list[datetime] = []
    page = 1
    while True:
        url = f"https://api.github.com/repos/{repository}/stargazers?per_page=100&page={page}"
        request = Request(
            url,
            headers={
                "Accept": "application/vnd.github.star+json",
                "Authorization": f"Bearer {token}",
                "User-Agent": "Mailcario-star-history",
                "X-GitHub-Api-Version": "2022-11-28",
            },
        )
        with urlopen(request, timeout=30) as response:
            batch = json.load(response)
        if not isinstance(batch, list):
            raise ValueError("GitHub did not return a stargazer list")
        dates.extend(datetime.fromisoformat(item["starred_at"].replace("Z", "+00:00")) for item in batch)
        if len(batch) < 100:
            break
        page += 1
    return sorted(dates)


def render(repository: str, dates: list[datetime]) -> str:
    width, height = 880, 300
    left, right, top, bottom = 72, 34, 66, 55
    plot_width, plot_height = width - left - right, height - top - bottom
    now = datetime.now(timezone.utc)
    start = dates[0] if dates else now
    span = max((now - start).total_seconds(), 1)
    maximum = max(len(dates), 1)
    points = [(left, top + plot_height)]
    for count, date in enumerate(dates, 1):
        x = left + (date - start).total_seconds() / span * plot_width
        y = top + plot_height - count / maximum * plot_height
        points.append((x, y))
    points.append((left + plot_width, top + plot_height - len(dates) / maximum * plot_height))
    path = " ".join(("M" if index == 0 else "L") + f"{x:.1f},{y:.1f}" for index, (x, y) in enumerate(points))
    escaped_repo = escape(repository)
    empty_message = (
        f'<text x="{width / 2:.0f}" y="158" text-anchor="middle" fill="#73838B" '
        'font-family="Segoe UI, Arial, sans-serif" font-size="17">The first stars will appear here</text>'
        if not dates else ""
    )
    return f'''<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}" role="img" aria-label="Star history for {escaped_repo}">
  <rect x="1" y="1" width="878" height="298" rx="12" fill="#FAFCFD" stroke="#DBE7EA"/>
  <text x="32" y="39" fill="#20343A" font-family="Segoe UI, Arial, sans-serif" font-size="21" font-weight="700">Star history</text>
  <text x="846" y="39" text-anchor="end" fill="#11747B" font-family="Segoe UI, Arial, sans-serif" font-size="19" font-weight="700">{len(dates)} stars</text>
  <path d="M{left},{top} V{top + plot_height} H{left + plot_width}" fill="none" stroke="#BFD0D5" stroke-width="1"/>
  <path d="M{left},{top + plot_height / 2:.1f} H{left + plot_width}" fill="none" stroke="#E2EBEE" stroke-width="1" stroke-dasharray="4 5"/>
  <path d="{path}" fill="none" stroke="#11747B" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/>
  {empty_message}
  <text x="{left}" y="275" fill="#73838B" font-family="Segoe UI, Arial, sans-serif" font-size="12">{start:%b %Y}</text>
  <text x="{left + plot_width}" y="275" text-anchor="end" fill="#73838B" font-family="Segoe UI, Arial, sans-serif" font-size="12">{now:%b %Y}</text>
</svg>
'''


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repository", default=os.environ.get("GITHUB_REPOSITORY", "Bouneh/Mailcario"))
    parser.add_argument("--offline", action="store_true", help="Render an empty chart before the first release")
    args = parser.parse_args()
    token = os.environ.get("GITHUB_TOKEN", "")
    if not args.offline and not token:
        parser.error("GITHUB_TOKEN is required for an online refresh")
    dates = [] if args.offline else fetch_stars(args.repository, token)
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(render(args.repository, dates), encoding="utf-8")
    print(f"Wrote {OUTPUT} ({len(dates)} stars)")


if __name__ == "__main__":
    main()
