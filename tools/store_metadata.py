"""Check and export local store-copy drafts; never upload or approve a listing."""
from __future__ import annotations

import argparse
from datetime import datetime
import hashlib
import json
from pathlib import Path
import re
import sys
import unicodedata
from urllib.parse import urlsplit

ROOT = Path(__file__).resolve().parent.parent
BRANDS = ("minecraft", "mojang", "creeper", "enderman", "illager", "redstone",
          "arch-illager", "squid coast", "creeper woods")
FIELDS = {
    "android": {"title": 30, "shortDescription": 80, "fullDescription": 4000},
    "ios": {"name": 30, "subtitle": 30, "promotionalText": 170, "description": 4000, "keywords": 100},
}
FILES = {
    "android": {"title": "title", "shortDescription": "short_description", "fullDescription": "full_description"},
    "ios": {"name": "name", "subtitle": "subtitle", "promotionalText": "promotional_text", "description": "description", "keywords": "keywords"},
}


def exact_keys(value, keys, label):
    if not isinstance(value, dict) or set(value) != set(keys):
        raise ValueError("Unexpected or missing fields: " + label)


def product_text(value, limit, label, byte_limit=False):
    if not isinstance(value, str) or not value.strip() or value != value.strip():
        raise ValueError("Nonempty trimmed text required: " + label)
    if unicodedata.normalize("NFC", value) != value or any(unicodedata.category(c).startswith("C") and c != "\n" for c in value):
        raise ValueError("Invalid text normalization/control characters: " + label)
    size = len(value.encode("utf-8")) if byte_limit else len(value)
    if size > limit or "<" in value or ">" in value:
        raise ValueError("Text limit/plain-text rule failed: " + label)
    folded = value.casefold()
    if any(term in folded for term in BRANDS) or re.search(r"\bcamp\b", folded):
        raise ValueError("Reserved product reference: " + label)
    return size


def preset_options(text, name):
    sections = re.split(r"(?m)(?=^\[preset\.\d+\]\r?$)", text)
    matching = [part for part in sections if re.search(r'^name="' + re.escape(name) + r'"\r?$', part, re.M)]
    if len(matching) != 1:
        raise ValueError("Expected exactly one export preset: " + name)
    options = {}
    for key, value in re.findall(r"^([^\[\s=][^=\r\n]*)=(.*)$", matching[0], re.M):
        if key in options:
            raise ValueError("Repeated export option: " + key)
        options[key] = value.strip().strip('"')
    return options


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("Repeated metadata key: " + key)
        result[key] = value
    return result


def copy_fingerprint(data):
    reviewed = {key: data[key] for key in ("locale", "publisher", "release", "android", "ios")}
    canonical = json.dumps(reviewed, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
    return hashlib.sha256(canonical.encode("utf-8")).hexdigest()


def validate(data, presets):
    exact_keys(data, ("formatVersion", "locale", "reviewStatus", "review", "publisher", "release", "android", "ios"), "listing")
    if type(data["formatVersion"]) is not int or data["formatVersion"] != 1 or data["locale"] != "en-US":
        raise ValueError("Unsupported metadata version/locale")
    if data["reviewStatus"] not in ("pending", "approved"):
        raise ValueError("Invalid review status")
    pending = []
    if data["reviewStatus"] == "pending":
        if data["review"] is not None:
            raise ValueError("A pending draft cannot contain an approval record")
        pending.append("human copy review")
    else:
        exact_keys(data["review"], ("reviewer", "approvedUtc", "reference", "copySha256"), "review")
        if any(not isinstance(v, str) or not v.strip() for v in data["review"].values()):
            raise ValueError("Incomplete human review record")
        approved = datetime.fromisoformat(data["review"]["approvedUtc"].replace("Z", "+00:00"))
        if approved.tzinfo is None or approved.utcoffset().total_seconds() != 0:
            raise ValueError("Review time must include UTC")
        if data["review"]["copySha256"] != copy_fingerprint(data):
            raise ValueError("Reviewed metadata changed; a new human review is required")
    counts = {}
    exports = {name: preset_options(presets, name) for name in ("Android", "Android Low", "iOS")}
    for platform, fields in FIELDS.items():
        identity = "applicationId" if platform == "android" else "bundleId"
        exact_keys(data[platform], (*fields, identity), platform)
        identifier = data[platform][identity]
        pattern = r"[A-Za-z][A-Za-z0-9_]*(?:\.[A-Za-z][A-Za-z0-9_]*)+" if platform == "android" else r"[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+"
        if not isinstance(identifier, str) or not re.fullmatch(pattern, identifier):
            raise ValueError("Invalid application identifier")
        setting = "package/unique_name" if platform == "android" else "application/bundle_identifier"
        names = ("Android", "Android Low") if platform == "android" else ("iOS",)
        if any(exports[name].get(setting) != identifier for name in names):
            raise ValueError("Store identifier differs from export presets: " + platform)
        counts[platform] = {name: product_text(data[platform][name], limit, platform + "." + name, platform == "ios" and name == "keywords") for name, limit in fields.items()}
    if data["android"]["title"] != data["ios"]["name"] or len(data["ios"]["name"]) < 2:
        raise ValueError("Store names must agree and contain at least two characters")
    keywords = data["ios"]["keywords"].split(",")
    name_words = set(re.findall(r"\w+", data["ios"]["name"].casefold()))
    if len(set(k.casefold() for k in keywords)) != len(keywords) or any(len(k.strip()) <= 2 or k != k.strip() or k.casefold() in name_words for k in keywords):
        raise ValueError("Invalid, repeated or app-name keywords")
    exact_keys(data["publisher"], ("name", "supportEmail", "supportUrl", "privacyPolicyUrl", "copyright"), "publisher")
    for name, value in data["publisher"].items():
        if value is None:
            pending.append("publisher." + name)
            continue
        if not isinstance(value, str) or not value.strip() or value != value.strip() or any(unicodedata.category(c).startswith("C") for c in value):
            raise ValueError("Invalid publisher field: " + name)
        if name.endswith("Url"):
            url = urlsplit(value)
            if url.scheme != "https" or not url.hostname or url.username or url.password or url.fragment:
                raise ValueError("Public HTTPS URL required: " + name)
        if name == "supportEmail" and not re.fullmatch(r"[^\s@]+@[^\s@]+\.[^\s@]+", value):
            raise ValueError("Invalid support email")
    exact_keys(data["release"], ("versionName", "androidVersionCode"), "release")
    version = data["release"]["versionName"]
    code = data["release"]["androidVersionCode"]
    if version is None:
        pending.append("release.versionName")
    elif not isinstance(version, str) or not re.fullmatch(r"\d+\.\d+\.\d+", version):
        raise ValueError("Release version must have three numeric components")
    if code is None:
        pending.append("release.androidVersionCode")
    elif type(code) is not int or not 1 <= code <= 2100000000:
        raise ValueError("Android version code outside upload range")
    if version and (any(exports[name].get("version/name") != version for name in ("Android", "Android Low")) or exports["iOS"].get("application/short_version") != version):
        raise ValueError("Release version differs from export presets")
    if code is not None and any(exports[name].get("version/code") != str(code) for name in ("Android", "Android Low")):
        raise ValueError("Release version code differs from export presets")
    return {"formatVersion": 1, "draftValid": True, "metadataReady": not pending, "copySha256": copy_fingerprint(data),
            "pending": pending, "fieldLengths": counts, "storeSubmissionReady": False,
            "physicalAcceptance": False,
            "limitations": ["Metadata syntax only: publisher ownership, URL reachability, store declarations, assets, signing and physical acceptance require independent evidence."]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("check", "export"))
    parser.add_argument("--source", type=Path, default=ROOT / "platform/store/en-US.json")
    parser.add_argument("--presets", type=Path, default=ROOT / "game/export_presets.cfg")
    parser.add_argument("--output", type=Path, help="New output directory for an export or check report")
    parser.add_argument("--require-ready", action="store_true")
    args = parser.parse_args()
    try:
        source = args.source.resolve()
        raw = source.read_bytes()
        if len(raw) > 64 * 1024 or raw.startswith(b"\xef\xbb\xbf") or b"\r" in raw:
            raise ValueError("Metadata must be bounded UTF-8 without BOM, using LF")
        data = json.loads(raw.decode("utf-8"), object_pairs_hook=unique_object)
        result = validate(data, args.presets.read_text(encoding="utf-8-sig"))
        result["sourceSha256"] = hashlib.sha256(raw).hexdigest()
        result["passed"] = result["draftValid"] and (not args.require_ready or result["metadataReady"])
        if args.command == "export" and args.output is None:
            raise ValueError("Export requires a new output directory")
        if args.output:
            output = args.output.resolve()
            if source.is_relative_to(output) or args.presets.resolve().is_relative_to(output):
                raise ValueError("Output must not contain either read-only input")
            output.mkdir(parents=True, exist_ok=False)
            if args.command == "export":
                for platform, fields in FILES.items():
                    folder = output / platform / data["locale"]
                    folder.mkdir(parents=True)
                    for field, filename in fields.items():
                        (folder / (filename + ".txt")).write_text(data[platform][field] + "\n", encoding="utf-8", newline="\n")
            (output / "result.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
        print(json.dumps(result))
        return 0 if result["passed"] else 1
    except (OSError, ValueError, TypeError, KeyError) as error:
        print(json.dumps({"passed": False, "status": "invalid-input", "message": str(error)}))
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
