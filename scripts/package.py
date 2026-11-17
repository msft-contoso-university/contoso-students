"""Publish a source-bound Students candidate; this script never deploys."""
import argparse
import hashlib
import json
import re
import subprocess
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    output = args.output.resolve()
    if output == ROOT or ROOT in output.parents or output.exists():
        raise ValueError("Output must be a new directory outside the source checkout")
    sha = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    if not re.fullmatch(r"[a-f0-9]{40}", sha):
        raise ValueError("An exact source commit is required")
    if subprocess.check_output(["git", "status", "--porcelain"], cwd=ROOT, text=True).strip():
        raise ValueError("Refusing to package a dirty source checkout")

    publish = output / "publish"
    output.mkdir(parents=True)
    subprocess.run(
        ["dotnet", "publish", str(ROOT / "src" / "Contoso.Students.Api" / "Contoso.Students.Api.csproj"),
         "-c", "Release", "--no-restore", "-o", str(publish), "-p:UseAppHost=false",
         "-p:ContinuousIntegrationBuild=true", f"-p:PathMap={ROOT}{'/'}=/_/"],
        cwd=ROOT, check=True,
    )
    files = sorted(path for path in publish.rglob("*") if path.is_file())
    if not (publish / "Contoso.Students.Api.dll").is_file() or not (publish / "seed-students.sql").is_file():
        raise ValueError("Published candidate is missing its entrypoint or seed script")
    private_forms = set()
    for path in (str(ROOT), str(Path.home())):
        for form in (path, path.replace("\\", "/")):
            private_forms.add(form.encode())
            private_forms.add(json.dumps(form)[1:-1].encode())
    for path in files:
        if any(form in path.read_bytes() for form in private_forms):
            raise ValueError(f"{path.name} embeds a private build path")

    manifest = {
        "service": "contoso-students",
        "sourceCommit": sha,
        "runtime": "net10.0",
        "frameworkDependent": True,
        "entrypoint": "dotnet Contoso.Students.Api.dll",
        "promotionOwner": "contoso-platform",
        "files": {
            path.relative_to(publish).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest()
            for path in files
        },
    }
    manifest_bytes = (json.dumps(manifest, indent=2) + "\n").encode()
    (publish / "candidate.json").write_bytes(manifest_bytes)
    target = output / f"contoso-students-{sha}.zip"
    with zipfile.ZipFile(target, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(path for path in publish.rglob("*") if path.is_file()):
            info = zipfile.ZipInfo(path.relative_to(publish).as_posix(), date_time=(2020, 1, 1, 0, 0, 0))
            info.external_attr = 0o100644 << 16
            info.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(info, path.read_bytes())
    (output / "candidate.json").write_bytes(manifest_bytes)
    checksum = hashlib.sha256(target.read_bytes()).hexdigest()
    (output / "SHA256SUMS").write_text(f"{checksum}  {target.name}\n", encoding="utf-8")
    print(f"Candidate source {sha}; ZIP SHA256 {checksum}")


if __name__ == "__main__":
    main()
