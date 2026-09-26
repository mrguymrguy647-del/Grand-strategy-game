#!/usr/bin/env python3
"""Creates missing Unity .meta files under Assets/ with stable GUIDs.

Unity creates .meta files itself, but files added outside the editor (by scripts, tools or
other people) get random GUIDs on every machine until someone commits them. Generating them
here, from the file path, keeps GUIDs identical for everyone.

Usage: python3 Tools/unity_meta.py
"""

import hashlib
import os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
ASSETS = os.path.join(ROOT, "Assets")

FOLDER = """fileFormatVersion: 2
guid: {guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""

SCRIPT = """fileFormatVersion: 2
guid: {guid}
MonoImporter:
  externalObjects: {{}}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {{instanceID: 0}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""

ASMDEF = """fileFormatVersion: 2
guid: {guid}
AssemblyDefinitionImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""

TEXT = """fileFormatVersion: 2
guid: {guid}
TextScriptImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""

DEFAULT = """fileFormatVersion: 2
guid: {guid}
DefaultImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""

# Anything else gets a minimal meta; Unity fills in its importer settings on import.
MINIMAL = """fileFormatVersion: 2
guid: {guid}
"""


def template_for(path, in_streaming_assets):
    if os.path.isdir(path):
        return FOLDER
    ext = os.path.splitext(path)[1].lower()
    if in_streaming_assets:
        return DEFAULT  # StreamingAssets files are copied as-is, never imported
    if ext == ".cs":
        return SCRIPT
    if ext == ".asmdef":
        return ASMDEF
    if ext in (".txt", ".json", ".md", ".csv", ".xml", ".bytes"):
        return TEXT
    return MINIMAL


def main():
    created = 0
    for dirpath, dirnames, filenames in os.walk(ASSETS):
        dirnames[:] = [d for d in dirnames if not d.startswith(".")]
        entries = [os.path.join(dirpath, d) for d in dirnames]
        entries += [os.path.join(dirpath, f) for f in filenames if not f.endswith(".meta") and not f.startswith(".")]
        for path in entries:
            meta = path + ".meta"
            if os.path.exists(meta):
                continue
            rel = os.path.relpath(path, ROOT).replace(os.sep, "/")
            guid = hashlib.md5(("grand-strategy:" + rel).encode("utf-8")).hexdigest()
            in_sa = "/StreamingAssets/" in "/" + rel + "/" and not rel.endswith("StreamingAssets")
            with open(meta, "w", encoding="utf-8", newline="\n") as f:
                f.write(template_for(path, in_sa).format(guid=guid))
            created += 1
    print(f"Created {created} .meta files")


if __name__ == "__main__":
    main()
