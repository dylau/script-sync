import zipfile
import os
import shutil

VSIX_DIR = r"C:\Users\uk083720\OneDrive\UniKassel\04_code\_WIP\script-sync_dl\VSCode\scriptsync"
OUT_VSIX = os.path.join(VSIX_DIR, "script-sync-1.2.28.vsix")
TMP_VSIX = os.path.join(VSIX_DIR, "package.zip")

# Required by VSIX spec: [Content_Types].xml at root
CONTENT_TYPES_XML = """<?xml version="1.0" encoding="utf-8"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="vsixmanifest" ContentType="text/xml" />
  <Default Extension="json" ContentType="application/json" />
  <Default Extension="js" ContentType="application/javascript" />
  <Default Extension="png" ContentType="image/png" />
  <Default Extension="md" ContentType="text/markdown" />
  <Default Extension="map" ContentType="application/json" />
</Types>"""

# Files to include (relative paths inside VSIX_DIR)
# These will be placed inside an 'extension/' subdirectory in the vsix,
# which is the layout yauzl expects.
files_to_include = [
    "extension.vsixmanifest",
    "package.json",
    "README.md",
    "CHANGELOG.md",
    "LICENSE.md",
    "logo/scriptsync_48.png",
    "logo/scriptsync_480.png",
    "out/extension.js",
    "out/extension.js.map",
]

# Remove old zip
if os.path.exists(TMP_VSIX):
    os.remove(TMP_VSIX)
if os.path.exists(OUT_VSIX):
    os.remove(OUT_VSIX)

with zipfile.ZipFile(TMP_VSIX, "w", zipfile.ZIP_STORED) as zf:
    # [Content_Types].xml at root
    zf.writestr("[Content_Types].xml", CONTENT_TYPES_XML)

    # All extension files go inside 'extension/' subdirectory
    for rel_path in files_to_include:
        full_path = os.path.join(VSIX_DIR, rel_path)
        if os.path.exists(full_path):
            zf.write(full_path, f"extension/{rel_path}")
            print(f"Added: extension/{rel_path}")
        else:
            print(f"SKIPPED (missing): {rel_path}")

# Rename to .vsix
shutil.move(TMP_VSIX, OUT_VSIX)
print(f"\nCreated: {OUT_VSIX}")
print(f"Size: {os.path.getsize(OUT_VSIX)} bytes")
