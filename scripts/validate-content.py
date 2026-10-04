#!/usr/bin/env python3
"""Validate the original starter pack without installing packages or changing files.

This checks the data contract. It cannot establish English correctness, difficulty,
copyright provenance, or the usefulness of a vocabulary sense; those need review.
"""

import argparse
from collections import Counter, defaultdict
import hashlib
import json
from pathlib import Path
import re
import sys
import unicodedata
import uuid


EXPECTED_COUNT = 300
EXPECTED_IDS_SHA256 = "f247c2f97c3ecaf1f552f2cc21c95c4cfee40002b0d237a7fb2e9ecf47575696"
ORIGIN_NOTE = "AI 編寫的原創學習教材；非官方 TOEIC 詞表"
CATEGORIES = {
    "一般職場", "會議與聯絡", "招募與人事", "訂購與採購", "財務與付款", "運送與物流",
    "旅行與交通", "住宿與餐飲", "設施與維修", "客戶與服務", "行銷與銷售", "工作流程與文件",
}
LEVELS = {"基礎", "優先", "延伸"}
PARTS_OF_SPEECH = {"名詞", "動詞", "形容詞", "副詞", "動詞片語", "名詞片語", "副詞片語", "介系詞片語"}
ITEM_FIELDS = {
    "id", "wordId", "headword", "partOfSpeech", "meaning", "cue", "level", "kind", "categories",
    "collocations", "examples", "origin", "enrollment", "isArchived", "isPaused", "isUserEdited",
}


def normalize(value):
    """Ignore case and spacing when looking for accidental duplicates."""
    return " ".join(unicodedata.normalize("NFKC", value).casefold().split())


def validate_pack(pack):
    """Return (errors, review_notes, summary); do not mutate the input."""
    errors, notes = [], []
    counts = {"items": 0, "categories": {}, "levels": {}, "kinds": {}}

    def problem(path, message):
        errors.append(f"{path}: {message}")

    def text(value, path, chinese=False):
        if not isinstance(value, str) or not value.strip():
            problem(path, "must be non-empty text")
            return False
        if value != value.strip() or "\ufffd" in value or any(ord(c) < 32 for c in value):
            problem(path, "contains outer whitespace, a replacement character, or a control character")
        if chinese and not re.search(r"[\u3400-\u9fff]", value):
            problem(path, "must include a Chinese explanation or translation")
        return True

    if not isinstance(pack, dict):
        return (["root: must be an object"], notes, counts)
    if set(pack) != {"packId", "version", "items"}:
        problem("root", "expected only packId, version, and items")
    if pack.get("packId") != "toeic-starter":
        problem("packId", "must be toeic-starter")
    if type(pack.get("version")) is not int or pack["version"] != 1:
        problem("version", "this validator checks starter edition 1")
    items = pack.get("items")
    if not isinstance(items, list):
        return (errors + ["items: must be an array"], notes, counts)
    # Each stored word owns its additional definitions; validate the original 300 sense IDs.
    stored_words = items
    word_ids = [x.get("wordId") for x in stored_words if isinstance(x, dict)]
    if len(stored_words) != 297 or len(set(word_ids)) != len(stored_words):
        problem("items", "expected 297 unique headwords with nested definitions")
    items = []
    for parent in stored_words:
        if not isinstance(parent, dict):
            items.append(parent)
            continue
        primary = {k: v for k, v in parent.items() if k != "additionalSenses"}
        items.append(primary)
        for definition in parent.get("additionalSenses", []):
            if not isinstance(definition, dict):
                items.append(definition)
            else:
                items.append({**primary, **definition})
    counts["items"] = len(items)
    if len(items) != EXPECTED_COUNT:
        problem("items", f"expected {EXPECTED_COUNT}, got {len(items)}")

    ids = set()
    sense_keys = set()
    headwords = defaultdict(list)
    meanings = defaultdict(list)
    sentences = set()
    categories, levels, kinds = Counter(), Counter(), Counter()
    for i, item in enumerate(items, 1):
        path = f"items[{i}]"
        if not isinstance(item, dict):
            problem(path, "must be an object")
            continue
        if set(item) != ITEM_FIELDS:
            problem(path, f"incorrect fields; missing={sorted(ITEM_FIELDS - set(item))}, extra={sorted(set(item) - ITEM_FIELDS)}")
        try:
            sense_id = uuid.UUID(item.get("id", ""))
            if str(sense_id) != item["id"] or sense_id.version != 5:
                problem(path + ".id", "must be a canonical lowercase UUIDv5")
            if sense_id in ids:
                problem(path + ".id", "duplicate sense ID")
            ids.add(sense_id)
        except (ValueError, TypeError, AttributeError):
            problem(path + ".id", "must be a UUID string")
        if isinstance(item.get("headword"), str):
            identity = "wording-headword-v1\n" + " ".join(unicodedata.normalize("NFKC", item["headword"]).split()).lower()
            expected_word_id = str(uuid.UUID(bytes_le=hashlib.sha256(identity.encode("utf-8")).digest()[:16]))
            if item.get("wordId") != expected_word_id:
                problem(path + ".wordId", "must identify the normalized headword, shared by all its senses")

        valid_text = {}
        for field in ("headword", "partOfSpeech", "meaning", "cue", "level", "kind"):
            valid_text[field] = text(item.get(field), path + "." + field, field == "meaning")
        if valid_text["partOfSpeech"] and item["partOfSpeech"] not in PARTS_OF_SPEECH:
            problem(path + ".partOfSpeech", "unknown part of speech")
        if valid_text["level"]:
            levels[item["level"]] += 1
            if item["level"] not in LEVELS:
                problem(path + ".level", "unknown suggested learning level")
        if valid_text["kind"]:
            kinds[item["kind"]] += 1
            if item["kind"] not in {"word", "phrase"}:
                problem(path + ".kind", "must be word or phrase")
            elif valid_text["headword"]:
                has_space = " " in item["headword"].strip()
                if (item["kind"] == "phrase") != has_space:
                    problem(path + ".kind", "word/phrase does not match the headword spacing")
        if all(valid_text[f] for f in ("headword", "partOfSpeech", "meaning", "cue")):
            key = tuple(normalize(item[f]) for f in ("headword", "partOfSpeech", "meaning"))
            if key in sense_keys:
                problem(path, "duplicate headword, part of speech, and meaning")
            sense_keys.add(key)
            headwords[key[0]].append(item)
            meanings[(key[1], key[2])].append(item["headword"])

        item_categories = item.get("categories")
        if not isinstance(item_categories, list) or not item_categories:
            problem(path + ".categories", "must be a non-empty array")
        else:
            seen_categories = set()
            for category in item_categories:
                if not text(category, path + ".categories"):
                    continue
                if category not in CATEGORIES:
                    problem(path + ".categories", f"unknown category {category}")
                if category in seen_categories:
                    problem(path + ".categories", "repeated category")
                seen_categories.add(category)
                categories[category] += 1

        collocations = item.get("collocations")
        if not isinstance(collocations, list) or not 1 <= len(collocations) <= 2:
            problem(path + ".collocations", "must contain one or two original examples of usage")
        else:
            valid = [normalize(c) for c in collocations if text(c, path + ".collocations")]
            if len(valid) != len(set(valid)):
                problem(path + ".collocations", "duplicate collocation")

        examples = item.get("examples")
        if not isinstance(examples, list) or not examples:
            problem(path + ".examples", "must contain at least one bilingual sentence")
        else:
            for j, example in enumerate(examples, 1):
                example_path = f"{path}.examples[{j}]"
                if not isinstance(example, dict) or set(example) != {"english", "chinese"}:
                    problem(example_path, "expected english and chinese fields")
                    continue
                if text(example["english"], example_path + ".english"):
                    sentence = normalize(example["english"])
                    if sentence in sentences:
                        problem(example_path, "an English sentence is reused elsewhere in the pack")
                    sentences.add(sentence)
                text(example["chinese"], example_path + ".chinese", chinese=True)

        if item.get("origin") != {"kind": "ai", "note": ORIGIN_NOTE}:
            problem(path + ".origin", "must identify the original AI-authored, unofficial starter material")
        if item.get("enrollment") != "Candidate":
            problem(path + ".enrollment", "new starter items must await the learner's selection")
        for field in ("isArchived", "isPaused", "isUserEdited"):
            if item.get(field) is not False:
                problem(path + "." + field, "must be the JSON boolean false")

    # Compare the whole set, not list positions: sorting must not change sense IDs.
    # Pin the shipped IDs directly, independently of the original generation method.
    ids_sha256 = hashlib.sha256("\n".join(sorted(map(str, ids))).encode("utf-8")).hexdigest()
    if ids_sha256 != EXPECTED_IDS_SHA256:
        problem("items.id", "edition 1 IDs have been added, lost, or regenerated")
    for word, senses in headwords.items():
        if len(senses) > 1:
            cues = {normalize(s["cue"]) for s in senses}
            definitions = {normalize(s["meaning"]) for s in senses}
            if len(cues) != len(senses) or len(definitions) != len(senses):
                problem(word, "multiple senses require different contextual cues and meanings")
            notes.append(f"Multiple senses to review: {word} ({len(senses)})")
    for (_, meaning), words in meanings.items():
        if len(words) > 1:
            notes.append(f"Possible synonym overlap (not necessarily an error): {', '.join(words)} = {meaning}")
    for category in sorted(CATEGORIES):
        if categories[category] < 25:
            problem("categories", f"expected at least 25 definitions in {category}, got {categories[category]}")
    counts.update(categories=dict(sorted(categories.items())), levels=dict(sorted(levels.items())),
                  kinds=dict(sorted(kinds.items())), distinctHeadwords=len(headwords), storedWords=len(stored_words))
    return errors, notes, counts


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("path", nargs="?", type=Path,
                        default=Path(__file__).resolve().parents[1] / "content" / "toeic-starter.json")
    args = parser.parse_args()
    try:
        pack = json.loads(args.path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        print(f"ERROR: cannot read starter pack: {error}")
        return 1
    errors, notes, counts = validate_pack(pack)
    print(json.dumps(counts, ensure_ascii=False, indent=2))
    for note in notes:
        print(f"REVIEW: {note}")
    for error in errors:
        print(f"ERROR: {error}")
    print(f"{'PASS' if not errors else 'FAIL'}: {counts['items']} items, {len(errors)} structural errors")
    return int(bool(errors))


if __name__ == "__main__":
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    raise SystemExit(main())
