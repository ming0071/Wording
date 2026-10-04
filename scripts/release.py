"""Publish only verified tag artifacts, staging attachments in a draft first."""
import argparse
from dataclasses import dataclass
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess
from urllib.error import HTTPError
from urllib.parse import quote, urlparse
from urllib.request import Request, build_opener, HTTPRedirectHandler
import xml.etree.ElementTree as ET

_spec = importlib.util.spec_from_file_location("verify_package", Path(__file__).with_name("verify-package.py"))
_validation = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_validation)
verify_package = _validation.verify_package

VERSION = re.compile(r"(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?")


def metadata(root, ref):
    version = ET.parse(root / "Directory.Build.props").findtext(".//Version", "").strip()
    if not VERSION.fullmatch(version):
        raise ValueError("Directory.Build.props must contain a semantic version.")
    release = ref.startswith("refs/tags/v")
    tag = "v" + version
    if release and ref != "refs/tags/" + tag:
        raise ValueError(f"Tag must match Directory.Build.props: expected {tag}.")
    if release and not (root / "docs/releases" / f"{version}.md").is_file():
        raise ValueError(f"Missing release notes: docs/releases/{version}.md")
    return {"version": version, "tag": tag, "release": "true" if release else "false"}


@dataclass(frozen=True)
class Asset:
    path: Path

    @property
    def name(self):
        return self.path.name

    @property
    def digest(self):
        with self.path.open("rb") as stream:
            return "sha256:" + hashlib.file_digest(stream, "sha256").hexdigest()

    def matches(self, remote):
        return remote.get("state") == "uploaded" and remote.get("digest") == self.digest and remote.get("size") == self.path.stat().st_size


def prepare(root, artifact_directory, ref):
    info = metadata(root, ref)
    if info["release"] != "true":
        raise ValueError("Publishing requires a version tag ref.")
    package = artifact_directory / f"Wording-{info['version']}-win-x64.zip"
    errors = verify_package(package, info["version"])
    if errors:
        raise ValueError("Package verification failed: " + "; ".join(errors))
    assets = [Asset(package), Asset(Path(str(package) + ".sha256")), Asset(artifact_directory / "toeic-vocabulary.json")]
    expected = (root / "content/toeic-vocabulary.json").read_bytes()
    if assets[2].path.read_bytes() != expected:
        raise ValueError("Vocabulary attachment differs from the tested source.")
    notes = (root / "docs/releases" / f"{info['version']}.md").read_text(encoding="utf-8-sig").strip()
    if not notes:
        raise ValueError("Release notes must not be empty.")
    return info, assets, notes


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise ValueError("Unexpected API redirect; refusing to forward credentials.")


class GitHub:
    def __init__(self, repository, token):
        if not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository) or not token:
            raise ValueError("GITHUB_REPOSITORY and GH_TOKEN are required.")
        self.base = f"https://api.github.com/repos/{repository}"
        self.token = token
        self.opener = build_opener(NoRedirect())

    def request(self, method, path, body=None, asset=None, missing_ok=False):
        url = path if path.startswith("https://") else self.base + path
        if urlparse(url).hostname not in {"api.github.com", "uploads.github.com"}:
            raise ValueError("Unexpected GitHub API host.")
        headers = {"Authorization": "Bearer " + self.token, "Accept": "application/vnd.github+json",
                   "User-Agent": "Wording-release", "X-GitHub-Api-Version": "2026-03-10"}
        data = None
        if asset:
            data = asset.path.read_bytes()
            headers["Content-Type"] = "application/octet-stream"
        elif body is not None:
            data = json.dumps(body).encode("utf-8")
            headers["Content-Type"] = "application/json"
        try:
            with self.opener.open(Request(url, data=data, headers=headers, method=method), timeout=180) as response:
                raw = response.read()
                return json.loads(raw) if raw else None
        except HTTPError as error:
            if error.code == 404 and missing_ok:
                return None
            raise ValueError(f"GitHub {method} failed (HTTP {error.code}); draft is retained for retry.") from None


def publish(api, tag, commit, assets, notes):
    if not re.fullmatch(r"[0-9a-f]{40}", commit):
        raise ValueError("Expected a checked-out commit SHA.")
    reference = api.request("GET", "/git/ref/tags/" + quote(tag, safe=""))
    target = reference["object"]
    for _ in range(5):
        if target["type"] != "tag":
            break
        target = api.request("GET", "/git/tags/" + target["sha"])["object"]
    if target["type"] != "commit" or target["sha"] != commit:
        raise ValueError("Remote tag does not point to the tested commit.")
    release = api.request("GET", "/releases/tags/" + quote(tag, safe=""), missing_ok=True)
    # The tag endpoint returns published releases only. Authenticated listing
    # also includes drafts, so a failed upload can resume the existing draft.
    if release is None:
        page = 1
        while True:
            entries = api.request("GET", f"/releases?per_page=100&page={page}")
            release = next((entry for entry in entries if entry["tag_name"] == tag), None)
            if release is not None or len(entries) < 100:
                break
            page += 1
    wanted = {asset.name for asset in assets}

    def verify_remote(remote):
        entries = remote.get("assets", [])
        return len(entries) == len(assets) and {entry["name"] for entry in entries} == wanted and all(
            asset.matches(next(entry for entry in entries if entry["name"] == asset.name)) for asset in assets)

    if release and not release["draft"]:
        if not verify_remote(release):
            raise ValueError("Published release differs; refusing to overwrite it.")
        return release["html_url"]
    payload = {"tag_name": tag, "target_commitish": commit, "name": "Wording " + tag,
               "body": notes, "draft": True, "prerelease": "-" in tag}
    if release:
        if any(entry["name"] not in wanted for entry in release.get("assets", [])):
            raise ValueError("Draft contains unexpected attachments; inspect it before retrying.")
        release = api.request("PATCH", f"/releases/{release['id']}", payload)
    else:
        release = api.request("POST", "/releases", payload)
    for asset in assets:
        existing = next((entry for entry in release.get("assets", []) if entry["name"] == asset.name), None)
        if existing and asset.matches(existing):
            continue
        if existing:
            api.request("DELETE", f"/releases/assets/{existing['id']}")
        uploaded = api.request("POST", release["upload_url"].split("{")[0] + "?name=" + quote(asset.name, safe=""), asset=asset)
        if not asset.matches(uploaded):
            raise ValueError(f"Server checksum or size mismatch: {asset.name}; draft retained.")
    verified = api.request("GET", f"/releases/{release['id']}")
    if not verify_remote(verified):
        raise ValueError("Draft attachments are incomplete; refusing to publish.")
    result = api.request("PATCH", f"/releases/{release['id']}",
                         {"draft": False, "make_latest": "false" if payload["prerelease"] else "legacy"})
    return result["html_url"]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=["metadata", "check", "publish"])
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parent.parent)
    parser.add_argument("--artifacts", type=Path, default=Path("artifacts/release"))
    args = parser.parse_args()
    ref = os.environ.get("GITHUB_REF", "")
    try:
        if args.mode == "metadata":
            info = metadata(args.root, ref)
            if os.environ.get("GITHUB_OUTPUT"):
                with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
                    output.write("".join(f"{key}={value}\n" for key, value in info.items()))
            print(json.dumps(info))
            return 0
        info, assets, notes = prepare(args.root, args.artifacts, ref)
        if args.mode == "check":
            print("Release preflight passed: " + ", ".join(asset.name for asset in assets))
            return 0
        commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=args.root, text=True).strip()
        api = GitHub(os.environ.get("GITHUB_REPOSITORY", ""), os.environ.get("GH_TOKEN", ""))
        url = publish(api, info["tag"], commit, assets, notes)
        print("Published release: " + url)
        if os.environ.get("GITHUB_STEP_SUMMARY"):
            with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as summary:
                summary.write(f"Release: [{info['tag']}]({url})\n")
        return 0
    except (OSError, ValueError, KeyError, ET.ParseError) as error:
        print("Release failed: " + str(error))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
