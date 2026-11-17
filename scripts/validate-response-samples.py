import argparse
import json
from pathlib import Path

import jsonschema
import yaml


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--samples", required=True, type=Path)
    args = parser.parse_args()
    contract = yaml.safe_load(
        (Path(__file__).resolve().parent.parent / "contracts" / "students.openapi.yaml").read_text(encoding="utf-8")
    )
    checker = jsonschema.FormatChecker()
    if "date-time" not in checker.checkers:
        raise RuntimeError("The RFC 3339 date-time format validator is unavailable.")
    schemas = {"Student", "StudentPage"}
    seen = set()
    for path in sorted(args.samples.glob("*.json")):
        sample = json.loads(path.read_text(encoding="utf-8"))
        if not isinstance(sample, dict) or set(sample) != {"schema", "body"} or sample["schema"] not in schemas:
            raise ValueError(f"Invalid response envelope: {path.name}")
        schema = {
            "$ref": f"#/components/schemas/{sample['schema']}",
            "components": contract["components"],
        }
        jsonschema.Draft4Validator(schema, format_checker=checker).validate(sample["body"])
        seen.add(sample["schema"])
    if seen != schemas:
        raise ValueError(f"Missing response schema coverage: {sorted(schemas - seen)}")
    print("Actual HTTP response samples conform to Students OpenAPI schemas.")


if __name__ == "__main__":
    main()
