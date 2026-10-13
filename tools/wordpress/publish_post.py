#!/usr/bin/env python3
"""
Publish docs/blog/<post>.html to a WordPress site as a DRAFT through the REST API.

  * uploads the images the page references (relative paths, or data: URIs) to the Media Library,
  * converts the page into plain post content (no <style>/<main>; the theme styles it),
  * creates the post as a draft, or updates the existing draft with the same slug.

By default it only creates/updates a DRAFT. Pass --publish to make the post live. A post that is
already published is left alone unless --allow-published is given.
Standard library only.

Credentials: the script ASKS for the WordPress user name and the Application Password when it runs
(the password is typed hidden). Nothing is saved to disk and nothing is read from files. For unattended
use only, WP_USER / WP_APP_PASSWORD environment variables are honoured if they happen to be set.

Examples (PowerShell):
    python tools\wordpress\publish_post.py --check
    python tools\wordpress\publish_post.py --dry-run
    python tools\wordpress\publish_post.py --categories "SQL Server" --tags "restore,backup,junior dba"
"""
from __future__ import annotations

import argparse
import base64
import getpass
import html
import json
import mimetypes
import os
import re
import sys
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
DEFAULT_POST = REPO / "docs" / "blog" / "sqlrestoresos-for-junior-dbas.html"
DEFAULT_SLUG = "sqlrestoresos-for-junior-dbas"


# --------------------------------------------------------------------------- conversion


def convert(page_html: str) -> tuple[str, str, str]:
    """Returns (title, excerpt, post_content_html). Image src values are left as found."""
    main = re.search(r"<main>(.*)</main>", page_html, re.S)
    body = main.group(1) if main else page_html

    h1 = re.search(r"<h1>(.*?)</h1>", body, re.S)
    title = html.unescape(re.sub(r"<[^>]+>", "", h1.group(1))).strip() if h1 else "SQLRestoreSOS"

    lede = re.search(r'<p class="lede">(.*?)</p>', body, re.S)
    excerpt = html.unescape(re.sub(r"<[^>]+>", "", lede.group(1))).strip() if lede else ""

    # Page furniture that WordPress supplies itself.
    body = re.sub(r'\s*<p class="kicker">.*?</p>', "", body, flags=re.S)
    body = re.sub(r"\s*<h1>.*?</h1>", "", body, flags=re.S)
    body = re.sub(r'\s*<p class="byline">.*?</p>', "", body, flags=re.S)
    body = re.sub(r'<p class="lede">', "<p><strong>", body)
    body = re.sub(r'(<p><strong>.*?)</p>', r"\1</strong></p>", body, count=1, flags=re.S)

    # Scenario tag + h2  ->  "Scenario 1: Restore ..." ; other tags are dropped.
    body = re.sub(r'<span class="tag">(Scenario \d)</span>\s*<h2>', r"<h2>\1: ", body)
    body = re.sub(r'<span class="tag">[^<]*</span>\s*', "", body)

    # Components that depended on the page's own CSS.
    body = re.sub(r'<ul class="why">', "<ul>", body)
    body = re.sub(r"<li><b>(.*?)</b><span>(.*?)</span></li>", r"<li><strong>\1</strong> \2</li>", body, flags=re.S)
    body = re.sub(r'<div class="callout">\s*(.*?)\s*</div>', r"<blockquote>\1</blockquote>", body, flags=re.S)
    body = re.sub(r'<span style="color:var\(--muted\)[^"]*">(.*?)</span>', r"\1", body)
    body = re.sub(r'<code style="[^"]*">', "<code>", body)
    body = re.sub(r'<div class="tablewrap">\s*(<table>.*?</table>)\s*</div>', r"\1", body, flags=re.S)
    body = re.sub(r'<section class="scenario">', "<section>", body)
    body = re.sub(r'<dl class="legend">', "<dl>", body)
    body = re.sub(r'<span class="(ok|bad|na)">(.*?)</span>', r"\2", body)

    # Figures -> core image block markup.
    def figure(m: re.Match) -> str:
        img = re.search(r"<img\b[^>]*>", m.group(0)).group(0)
        src = re.search(r'src="([^"]+)"', img).group(1)
        alt = re.search(r'alt="([^"]*)"', img)
        cap = re.search(r"<figcaption>(.*?)</figcaption>", m.group(0), re.S)
        return (
            '<figure class="wp-block-image"><img src="%s" alt="%s"/>%s</figure>'
            % (src, alt.group(1) if alt else "", "<figcaption>%s</figcaption>" % cap.group(1) if cap else "")
        )

    body = re.sub(r'<figure class="figure">.*?</figure>', figure, body, flags=re.S)

    # Page-only chrome.
    body = re.sub(r"<footer>(.*?)</footer>", r"<hr/>\1", body, flags=re.S)
    body = re.sub(r"[ \t]+\n", "\n", body).strip()

    return title, excerpt, "<!-- wp:html -->\n" + body + "\n<!-- /wp:html -->"


# --------------------------------------------------------------------------- REST client


class Wp:
    def __init__(self, base: str, user: str, password: str) -> None:
        self.base = base.rstrip("/")
        token = base64.b64encode(f"{user}:{password}".encode()).decode()
        self.auth = f"Basic {token}"

    def call(self, method: str, route: str, *, params: dict | None = None, json_body=None,
             data: bytes | None = None, headers: dict | None = None):
        url = f"{self.base}/wp-json/wp/v2/{route.lstrip('/')}"
        if params:
            url += "?" + urllib.parse.urlencode(params)
        hdrs = {"Authorization": self.auth, "Accept": "application/json", "User-Agent": "SQLRestoreSOS-publisher/1.0"}
        if json_body is not None:
            data = json.dumps(json_body).encode()
            hdrs["Content-Type"] = "application/json"
        if headers:
            hdrs.update(headers)
        req = urllib.request.Request(url, data=data, headers=hdrs, method=method)
        try:
            with urllib.request.urlopen(req, timeout=120) as resp:
                raw = resp.read()
                return json.loads(raw) if raw else None
        except urllib.error.HTTPError as e:
            detail = e.read().decode("utf-8", "replace")[:400]
            hint = {
                401: "Check WP_USER / WP_APP_PASSWORD (use the user NAME, not the e-mail address, unless that is your login).",
                403: "The account may lack rights, or a security plugin / firewall blocks the REST API.",
                404: "REST API not found: is WP_URL correct and is the REST API enabled?",
            }.get(e.code, "")
            sys.exit(f"{method} {route} failed: HTTP {e.code}\n{detail}\n{hint}")
        except urllib.error.URLError as e:
            sys.exit(f"Could not reach {self.base}: {e.reason}")


def find_or_create_terms(wp: Wp, taxonomy: str, names: list[str]) -> list[int]:
    ids = []
    for name in names:
        found = wp.call("GET", taxonomy, params={"search": name, "per_page": 100})
        exact = [t for t in found if t["name"].lower() == name.lower()]
        if exact:
            ids.append(exact[0]["id"])
        else:
            ids.append(wp.call("POST", taxonomy, json_body={"name": name})["id"])
    return ids


def upload_image(wp: Wp, src: str, post_dir: Path, alt: str) -> str:
    """Uploads one image (relative file or data: URI) and returns its public URL. Re-uses an earlier upload."""
    if src.startswith("data:"):
        header, b64 = src.split(",", 1)
        mime = header[5:].split(";")[0]
        content = base64.b64decode(b64)
        stem = "sqlrestoresos-inline-%d" % abs(hash(b64[:200]))
        filename = f"{stem}{mimetypes.guess_extension(mime) or '.png'}"
    else:
        path = (post_dir / src).resolve()
        if not path.is_file():
            sys.exit(f"Image not found: {path}")
        content = path.read_bytes()
        mime = mimetypes.guess_type(path.name)[0] or "application/octet-stream"
        filename = "sqlrestoresos-" + re.sub(r"[^a-z0-9._-]+", "-", path.name.lower())
    slug = Path(filename).stem

    existing = wp.call("GET", "media", params={"slug": slug})
    if existing:
        return existing[0]["source_url"]

    media = wp.call("POST", "media", data=content, headers={
        "Content-Type": mime,
        "Content-Disposition": f'attachment; filename="{filename}"',
    })
    if alt:
        wp.call("POST", f"media/{media['id']}", json_body={"alt_text": html.unescape(alt)})
    return media["source_url"]


# --------------------------------------------------------------------------- main


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--post", type=Path, default=DEFAULT_POST, help="source HTML (default: %(default)s)")
    ap.add_argument("--slug", default=DEFAULT_SLUG)
    ap.add_argument("--categories", default="", help="comma separated names; created when missing")
    ap.add_argument("--tags", default="", help="comma separated names; created when missing")
    ap.add_argument("--url", default=os.environ.get("WP_URL", "https://dbaronald.nl"), help="site root (default: %(default)s)")
    ap.add_argument("--user", default=os.environ.get("WP_USER"), help="WordPress user name (asked for when omitted)")
    ap.add_argument("--check", action="store_true", help="only test the credentials, then exit")
    ap.add_argument("--dry-run", action="store_true", help="convert and write a local preview; change nothing on the site")
    ap.add_argument("--publish", action="store_true", help="publish the post (default: leave it as a draft)")
    ap.add_argument("--allow-published", action="store_true", help="allow updating a post that is already published")
    args = ap.parse_args()

    page = args.post.read_text(encoding="utf-8")
    title, excerpt, content = convert(page)

    if args.dry_run:
        out = REPO / "docs" / "blog" / (args.slug + ".wordpress-preview.html")
        out.write_text(content, encoding="utf-8")
        imgs = re.findall(r'<img src="([^"]+)"', content)
        print(f"Title   : {title}\nExcerpt : {excerpt[:90]}...\nImages  : {len(imgs)}\nWrote   : {out}")
        return

    base = args.url
    user = args.user or input(f"WordPress user name for {base}: ").strip()
    password = os.environ.get("WP_APP_PASSWORD") or getpass.getpass("Application Password (input hidden): ").strip()
    if not user or not password:
        sys.exit("A user name and an Application Password are required.")
    wp = Wp(base, user, password)

    me = wp.call("GET", "users/me", params={"context": "edit"})
    print(f"Authenticated as {me.get('name')} (id {me.get('id')}) on {base}")
    if args.check:
        return

    # Images first, then rewrite the page to their Media Library URLs.
    post_dir = args.post.parent
    for m in set(re.finditer(r'<img src="([^"]+)" alt="([^"]*)"', content)):
        src, alt = m.group(1), m.group(2)
        if src.startswith("http"):
            continue
        url = upload_image(wp, src, post_dir, alt)
        content = content.replace(f'src="{src}"', f'src="{url}"')
        print(f"  uploaded {Path(src).name if not src.startswith('data:') else 'inline image'}")

    body = {"title": title, "slug": args.slug, "content": content, "excerpt": excerpt}
    if args.categories:
        body["categories"] = find_or_create_terms(wp, "categories", [c.strip() for c in args.categories.split(",") if c.strip()])
    if args.tags:
        body["tags"] = find_or_create_terms(wp, "tags", [t.strip() for t in args.tags.split(",") if t.strip()])

    existing = wp.call("GET", "posts", params={"slug": args.slug, "status": "any", "context": "edit"})
    if existing:
        post = existing[0]
        if post["status"] == "publish" and not args.allow_published:
            sys.exit(f"'{args.slug}' is already published ({post['link']}). Re-run with --allow-published to overwrite it.")
        if args.publish:
            body["status"] = "publish"  # otherwise the status is not sent and stays as it is
        result = wp.call("POST", f"posts/{post['id']}", json_body=body)
        print(f"Updated existing post #{result['id']} (status: {result['status']})")
    else:
        body["status"] = "publish" if args.publish else "draft"
        result = wp.call("POST", "posts", json_body=body)
        print(f"Created post #{result['id']} (status: {result['status']})")

    edit = f"{base.rstrip('/')}/wp-admin/post.php?post={result['id']}&action=edit"
    if result["status"] == "publish":
        print(f"Live at:\n  {result['link']}\nEdit it here:\n  {edit}")
    else:
        print(f"Review and publish it here:\n  {edit}")


if __name__ == "__main__":
    main()
