"""Prepare a private project-only iOS preset without changing the source checkout."""
import argparse
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET


def prepare(text, team):
    if not re.fullmatch(r"[A-Z0-9]{10}", team):
        raise ValueError("Supply the actual ten-character Apple team ID")
    sections = re.split(r"(?m)(?=^\[preset\.\d+\]\r?$)", text)
    indexes = [i for i, part in enumerate(sections) if re.search(r'^name="iOS"\r?$', part, re.M)]
    if len(indexes) != 1:
        raise ValueError("Expected exactly one iOS preset")
    index = indexes[0]
    ios = sections[index]
    if not re.search(r'^architectures/arm64=true\r?$', ios, re.M) or not re.search(r'^application/min_ios_version="14\.0"\r?$', ios, re.M):
        raise ValueError("The specified iOS 14/arm64 contract is missing")
    match = re.search(r'^application/additional_plist_content=(.*)\r?$', ios, re.M)
    if match:
        fragment = json.loads(match[1])
        dictionary = ET.fromstring("<dict>" + fragment + "</dict>")
        children = list(dictionary)
        if len(children) % 2 or any(children[i].tag != "key" for i in range(0, len(children), 2)):
            raise ValueError("Malformed additional plist dictionary")
        retained = []
        names = set()
        for i in range(0, len(children), 2):
            key, value = children[i:i + 2]
            if key.text in names:
                raise ValueError("Repeated additional plist key")
            names.add(key.text)
            if key.text in ("UIFileSharingEnabled", "LSSupportsOpeningDocumentsInPlace"):
                if value.tag not in ("true", "false"):
                    raise ValueError("Document sharing plist keys must be booleans")
            else:
                retained.extend((ET.tostring(key, encoding="unicode"), ET.tostring(value, encoding="unicode")))
        line = "application/additional_plist_content=" + json.dumps("".join(retained), ensure_ascii=False)
        ios = ios[:match.start()] + line + ios[match.end():]
    values = {
        "application/app_store_team_id": json.dumps(team),
        "application/export_project_only": "true",
        "user_data/accessible_from_files_app": "true",
        "user_data/accessible_from_itunes_sharing": "true",
    }
    for key, value in values.items():
        pattern = r"^" + re.escape(key) + r"=.*\r?\n?"
        ios = re.sub(pattern, "", ios, flags=re.M)
        ios = ios.replace("architectures/arm64=true", "architectures/arm64=true\n" + key + "=" + value, 1)
    sections[index] = ios
    return "".join(sections)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--team", required=True)
    args = parser.parse_args()
    try:
        if args.source.resolve() == args.output.resolve():
            raise ValueError("Prepared preset must use a new output path")
        text = args.source.read_bytes().decode("utf-8")
        result = prepare(text, args.team)
        with args.output.open("x", encoding="utf-8", newline="\n") as stream:
            stream.write(result)
        print(json.dumps({"prepared": True, "deploymentTarget": "14.0", "projectOnly": True, "physicalAcceptance": False}))
        return 0
    except (OSError, ValueError, TypeError, ET.ParseError) as error:
        print(json.dumps({"prepared": False, "failure": str(error)}))
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
