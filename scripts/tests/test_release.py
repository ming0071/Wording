import copy
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import release
import test_verify_package as package_fixtures


COMMIT = "a" * 40


class FakeGitHub:
    def __init__(self, existing=None):
        self.release = copy.deepcopy(existing)
        self.calls = []
        self.target = {"type": "commit", "sha": COMMIT}
        self.wrong_upload = False
        self.omit_final_asset = False
        self.list_prefix = []

    def request(self, method, path, body=None, asset=None, missing_ok=False):
        self.calls.append((method, path, copy.deepcopy(body)))
        if path.startswith("/git/ref/tags/"):
            return {"object": self.target}
        if path.startswith("/git/tags/"):
            return {"object": {"type": "commit", "sha": COMMIT}}
        if path.startswith("/releases/tags/"):
            return copy.deepcopy(self.release) if self.release and not self.release["draft"] else None
        if path.startswith("/releases?per_page=100&page="):
            page = int(path.rsplit("=", 1)[-1])
            entries = self.list_prefix + ([self.release] if self.release else [])
            return copy.deepcopy(entries[(page - 1) * 100:page * 100])
        if method == "POST" and path == "/releases":
            self.release = dict(body, id=1, assets=[], upload_url="https://uploads.github.com/repos/test/repo/releases/1/assets{?name,label}", html_url="https://github.com/test/repo/releases/tag/v0.3.0")
        elif method == "PATCH":
            self.release.update(body)
        elif method == "DELETE":
            identifier = int(path.rsplit("/", 1)[-1])
            self.release["assets"] = [entry for entry in self.release["assets"] if entry["id"] != identifier]
            return None
        elif asset:
            uploaded = {"id": len(self.calls), "name": asset.name, "size": asset.path.stat().st_size,
                        "digest": "sha256:wrong" if self.wrong_upload else asset.digest, "state": "uploaded"}
            self.release["assets"].append(uploaded)
            return copy.deepcopy(uploaded)
        result = copy.deepcopy(self.release)
        if method == "GET" and self.omit_final_asset:
            result["assets"] = result["assets"][:-1]
        return result


class ReleaseTests(unittest.TestCase):
    def setUp(self):
        fixture = package_fixtures.PackageVerificationTests()
        fixture.setUp()
        self.addCleanup(fixture.doCleanups)
        fixture.write_package()
        self.root = Path(fixture.directory.name)
        (self.root / "Directory.Build.props").write_text("<Project><PropertyGroup><Version>0.3.0</Version></PropertyGroup></Project>", encoding="utf-8")
        (self.root / "content").mkdir()
        (self.root / "content/toeic-vocabulary.json").write_text('{"version":1,"items":[]}', encoding="utf-8")
        (self.root / "toeic-vocabulary.json").write_bytes((self.root / "content/toeic-vocabulary.json").read_bytes())
        (self.root / "docs/releases").mkdir(parents=True)
        (self.root / "docs/releases/0.3.0.md").write_text("Reviewed release notes.", encoding="utf-8")
        self.info, self.assets, self.notes = release.prepare(self.root, self.root, "refs/tags/v0.3.0")

    def remote(self, draft=False):
        return {"id": 1, "tag_name": "v0.3.0", "draft": draft, "assets": [{"id": i, "name": asset.name, "size": asset.path.stat().st_size,
                "digest": asset.digest, "state": "uploaded"} for i, asset in enumerate(self.assets, 10)],
                "upload_url": "https://uploads.github.com/repos/test/repo/releases/1/assets{?name,label}",
                "html_url": "https://github.com/test/repo/releases/tag/v0.3.0"}

    def assert_not_published(self, api):
        self.assertFalse(any(body and body.get("draft") is False for method, path, body in api.calls))

    def test_metadata_only_releases_matching_version_tags(self):
        self.assertEqual("false", release.metadata(self.root, "refs/heads/main")["release"])
        self.assertEqual("false", release.metadata(self.root, "refs/tags/backup")["release"])
        self.assertEqual("true", self.info["release"])
        for ref in ("refs/tags/v0.4.0", "refs/tags/v0.3.0\nrelease=true"):
            with self.subTest(ref=ref), self.assertRaises(ValueError):
                release.metadata(self.root, ref)

    def test_tag_requires_release_notes_and_valid_version(self):
        (self.root / "docs/releases/0.3.0.md").unlink()
        with self.assertRaisesRegex(ValueError, "Missing release notes"):
            release.metadata(self.root, "refs/tags/v0.3.0")
        (self.root / "Directory.Build.props").write_text("<Project><Version>../bad</Version></Project>")
        with self.assertRaisesRegex(ValueError, "semantic version"):
            release.metadata(self.root, "refs/heads/main")

    def test_preflight_requires_tag_and_verified_package(self):
        with self.assertRaises(ValueError):
            release.prepare(self.root, self.root, "refs/heads/main")
        self.assets[0].path.write_bytes(b"broken zip")
        with self.assertRaisesRegex(ValueError, "Package verification failed"):
            release.prepare(self.root, self.root, "refs/tags/v0.3.0")

    def test_vocabulary_and_notes_must_match_reviewed_source(self):
        self.assets[2].path.write_bytes(b"wrong vocabulary")
        with self.assertRaisesRegex(ValueError, "differs from the tested source"):
            release.prepare(self.root, self.root, "refs/tags/v0.3.0")
        self.assets[2].path.write_bytes((self.root / "content/toeic-vocabulary.json").read_bytes())
        (self.root / "docs/releases/0.3.0.md").write_text(" ")
        with self.assertRaisesRegex(ValueError, "must not be empty"):
            release.prepare(self.root, self.root, "refs/tags/v0.3.0")

    def test_new_release_uploads_all_checksums_before_publication(self):
        api = FakeGitHub()
        url = release.publish(api, "v0.3.0", COMMIT, self.assets, self.notes)
        self.assertTrue(url.startswith("https://github.com/"))
        self.assertFalse(api.release["draft"])
        self.assertEqual("legacy", api.release["make_latest"])
        self.assertEqual({asset.name for asset in self.assets}, {entry["name"] for entry in api.release["assets"]})
        self.assertEqual(("PATCH", "/releases/1", {"draft": False, "make_latest": "legacy"}), api.calls[-1])

    def test_annotated_tag_is_resolved_to_checked_out_commit(self):
        api = FakeGitHub()
        api.target = {"type": "tag", "sha": "b" * 40}
        release.publish(api, "v0.3.0", COMMIT, self.assets, self.notes)
        self.assertIn(("GET", "/git/tags/" + "b" * 40, None), api.calls)

    def test_wrong_commit_cannot_create_or_publish_release(self):
        api = FakeGitHub()
        api.target = {"type": "commit", "sha": "b" * 40}
        with self.assertRaisesRegex(ValueError, "tested commit"):
            release.publish(api, "v0.3.0", COMMIT, self.assets, self.notes)
        self.assertTrue(all(method == "GET" for method, path, body in api.calls))

    def test_prerelease_does_not_become_latest(self):
        api = FakeGitHub()
        release.publish(api, "v0.4.0-beta.1", COMMIT, self.assets, self.notes)
        self.assertTrue(api.release["prerelease"])
        self.assertEqual("false", api.release["make_latest"])

    def test_identical_published_release_is_read_only_on_retry(self):
        api = FakeGitHub(self.remote())
        release.publish(api, "v0.3.0", COMMIT, self.assets, self.notes)
        self.assertTrue(all(method == "GET" for method, path, body in api.calls))

    def test_published_release_is_never_overwritten(self):
        remote = self.remote()
        remote["assets"][0]["digest"] = "sha256:wrong"
        api = FakeGitHub(remote)
        with self.assertRaisesRegex(ValueError, "refusing to overwrite"):
            release.publish(api, "v0.3.0", COMMIT, self.assets, self.notes)
        self.assertTrue(all(method == "GET" for method, path, body in api.calls))

    def test_draft_retry_skips_verified_assets_and_replaces_bad_draft_asset(self):
        remote = self.remote(draft=True)
        remote["assets"][0]["digest"] = "sha256:wrong"
        api = FakeGitHub(remote)
        release.publish(api, "v0.3.0", COMMIT, self.assets, self.notes)
        self.assertEqual(1, sum(method == "DELETE" for method, path, body in api.calls))
        self.assertEqual(1, sum(method == "POST" for method, path, body in api.calls))
        self.assertFalse(api.release["draft"])

    def test_unexpected_draft_asset_requires_inspection(self):
        remote = self.remote(draft=True)
        remote["assets"].append({"name": "unexpected.db"})
        api = FakeGitHub(remote)
        with self.assertRaisesRegex(ValueError, "unexpected attachments"):
            release.publish(api, "v0.3.0", COMMIT, self.assets, self.notes)
        self.assertTrue(all(method == "GET" for method, path, body in api.calls))

    def test_draft_retry_finds_older_draft_without_creating_duplicate(self):
        api = FakeGitHub(self.remote(draft=True))
        api.list_prefix = [{"tag_name": f"v0.4.{index}", "draft": False} for index in range(100)]
        release.publish(api, "v0.3.0", COMMIT, self.assets, self.notes)
        self.assertIn(("GET", "/releases?per_page=100&page=2", None), api.calls)
        self.assertFalse(any(method == "POST" for method, path, body in api.calls))
        self.assertFalse(api.release["draft"])

    def test_upload_digest_failure_leaves_private_draft(self):
        api = FakeGitHub()
        api.wrong_upload = True
        with self.assertRaisesRegex(ValueError, "checksum or size mismatch"):
            release.publish(api, "v0.3.0", COMMIT, self.assets, self.notes)
        self.assertTrue(api.release["draft"])
        self.assert_not_published(api)

    def test_incomplete_final_assets_cannot_be_published(self):
        api = FakeGitHub()
        api.omit_final_asset = True
        with self.assertRaisesRegex(ValueError, "incomplete"):
            release.publish(api, "v0.3.0", COMMIT, self.assets, self.notes)
        self.assert_not_published(api)

    def test_client_rejects_missing_credentials_and_unexpected_hosts(self):
        with self.assertRaises(ValueError):
            release.GitHub("test/repo", "")
        api = release.GitHub("test/repo", "fixture-token")
        with self.assertRaisesRegex(ValueError, "Unexpected GitHub API host"):
            api.request("POST", "https://example.com/upload")


if __name__ == "__main__":
    unittest.main()
