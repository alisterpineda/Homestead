#!/usr/bin/env python3
"""OPTIONAL high-fidelity raster anonymization via an Azure image-edit model.

For scanned / image documents, replaces each PII region by sending a small CROP
of that region (plus a mask over just the text) to an Azure image-edit model
(e.g. gpt-image-1) and pasting the returned patch back into the original image.
The model matches the surrounding font *style* and background far better than the
local cover-and-reinsert path.

It is GENERATIVE, so it is NOT pixel/font-perfect and MUST still be verified:
  - It matches font style, not the exact typeface.
  - It can leave a faint trace of the original or render the wrong value.
  - Numbers are especially prone to garbling.

Why crop-and-paste (not a whole-page edit): image-edit models emit a fixed set of
output sizes and regenerate the whole canvas, which would rescale the page and
drift every pixel. Editing a small masked crop confines both effects to the patch;
the rest of the page stays byte-identical.

CONFIG — read ONLY from a gitignored <skill>/.env file (never committed), as
KEY=VALUE lines:
    ANON_AZURE_IMAGE_ENDPOINT=https://<resource>.openai.azure.com
    ANON_AZURE_IMAGE_KEY=<resource key>
    ANON_AZURE_IMAGE_DEPLOYMENT=<image-edit deployment name>
    ANON_AZURE_IMAGE_APIVERSION=<optional; defaults below>

PRIVACY: this UPLOADS the un-anonymized crop to Azure. Do not use it for data that
must not leave the machine — the local path keeps everything on-device.

VERIFICATION: always run verify.py on the OUTPUT afterwards (re-OCR: originals
gone AND intended replacements present) and eyeball the render.

Usage:
    redact_image_ai.py INPUT.png OUTPUT.png REPLACEMENTS.json
Exit codes: 0 ok · 2 not configured (caller should fall back to local) · 1 error

NOTE: this is a reference implementation. Validate against your live deployment —
especially the output size/aspect handling (see resize below) and the exact
endpoint / api-version — before relying on it.
"""
import base64
import csv
import io
import json
import os
import subprocess
import sys

DEFAULT_API_VERSION = "2025-04-01-preview"
CONTEXT_PAD = 24  # px of surrounding context sent with each crop


def load_config():
    """Read config ONLY from the gitignored <skill>/.env. None if missing/incomplete."""
    skill_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    path = os.path.join(skill_root, ".env")
    if not os.path.exists(path):
        return None

    env = {}
    for line in open(path):
        line = line.strip()
        if line and not line.startswith("#") and "=" in line:
            k, v = line.split("=", 1)
            env[k.strip()] = v.strip().strip('"').strip("'")

    cfg = {
        "endpoint": env.get("ANON_AZURE_IMAGE_ENDPOINT"),
        "key": env.get("ANON_AZURE_IMAGE_KEY"),
        "deployment": env.get("ANON_AZURE_IMAGE_DEPLOYMENT"),
        "api_version": env.get("ANON_AZURE_IMAGE_APIVERSION", DEFAULT_API_VERSION),
    }
    return cfg if cfg["endpoint"] and cfg["key"] and cfg["deployment"] else None


def ocr_words(path):
    """List of (text, x0, y0, x1, y1, line_id) from tesseract TSV."""
    out = subprocess.run(["tesseract", path, "stdout", "tsv"],
                         capture_output=True, text=True).stdout
    words = []
    # Tesseract TSV is unquoted; QUOTE_NONE stops a stray `"` in OCR text from
    # being read as a quote char and merging rows.
    for row in csv.DictReader(io.StringIO(out), delimiter="\t", quoting=csv.QUOTE_NONE):
        text = (row.get("text") or "").strip()
        if not text:
            continue
        l, t, w, h = (int(row["left"]), int(row["top"]),
                      int(row["width"]), int(row["height"]))
        line_id = (row["block_num"], row["par_num"], row["line_num"])
        words.append((text, l, t, l + w, t + h, line_id))
    return words


def locate(words, target):
    """Union bbox of the consecutive words on one line that spell `target`."""
    norm = " ".join(target.split())
    by_line = {}
    for w in words:
        by_line.setdefault(w[5], []).append(w)
    for line in by_line.values():
        for i in range(len(line)):
            joined = ""
            for j in range(i, len(line)):
                joined = (joined + " " + line[j][0]).strip()
                if norm in joined:
                    grp = line[i:j + 1]
                    return (min(g[1] for g in grp), min(g[2] for g in grp),
                            max(g[3] for g in grp), max(g[4] for g in grp))
                if len(joined) > len(norm) + 40:
                    break
    return None


def make_mask(size, edit_box):
    """Opaque (keep) everywhere; transparent (edit) over edit_box — OpenAI mask
    semantics: alpha==0 marks the region to regenerate."""
    from PIL import Image, ImageDraw
    m = Image.new("RGBA", size, (0, 0, 0, 255))
    ImageDraw.Draw(m).rectangle(edit_box, fill=(0, 0, 0, 0))
    return m


def _v1_base(endpoint):
    """Resource-root OpenAI v1 surface from the configured endpoint. Azure AI
    Foundry serves image edits at <resource>/openai/v1/ with an api-key header —
    NOT the legacy /openai/deployments/...?api-version= route (404s here), and it
    strips the project path (/api/projects/...)."""
    e = endpoint.split("/api/")[0].rstrip("/")
    if e.endswith("/openai/v1"):
        return e + "/"
    if e.endswith("/openai"):
        return e + "/v1/"
    return e + "/openai/v1/"


def azure_edit(cfg, crop, mask, replacement):
    from openai import OpenAI
    client = OpenAI(base_url=_v1_base(cfg["endpoint"]), api_key=cfg["key"],
                    default_headers={"api-key": cfg["key"]})

    def _png(img, name):
        b = io.BytesIO(); img.save(b, format="PNG"); b.name = name; b.seek(0); return b

    prompt = (f"Replace the text in the edited region with exactly: {replacement}. "
              f"Match the font, size, weight, color, alignment, and background of "
              f"the surrounding text. Change nothing else; do not keep or add any "
              f"other text.")
    res = client.images.edit(model=cfg["deployment"], prompt=prompt,
                             image=_png(crop, "image.png"), mask=_png(mask, "mask.png"))
    return base64.b64decode(res.data[0].b64_json)


def main():
    if len(sys.argv) != 4:
        sys.exit(__doc__)
    inp, outp, repl_path = sys.argv[1:4]

    cfg = load_config()
    if not cfg:
        print("Azure image-edit not configured (create a gitignored "
              ".claude/skills/document-anonymizer/.env with ANON_AZURE_IMAGE_ENDPOINT/"
              "_KEY/_DEPLOYMENT). Falling back: use the local cover-and-reinsert path.",
              file=sys.stderr)
        sys.exit(2)

    from PIL import Image
    replacements = json.load(open(repl_path))
    img = Image.open(inp).convert("RGB")
    words = ocr_words(inp)

    for original, replacement in replacements.items():
        box = locate(words, original)
        if not box:
            print(f"  [!! NOT LOCATED] {original!r} — OCR didn't find it; handle it "
                  f"manually and re-verify.", file=sys.stderr)
            continue
        x0, y0, x1, y1 = box
        rx0, ry0 = max(0, x0 - CONTEXT_PAD), max(0, y0 - CONTEXT_PAD)
        rx1, ry1 = min(img.width, x1 + CONTEXT_PAD), min(img.height, y1 + CONTEXT_PAD)
        crop = img.crop((rx0, ry0, rx1, ry1))
        mask = make_mask(crop.size, (x0 - rx0, y0 - ry0, x1 - rx0, y1 - ry0))
        edited = Image.open(io.BytesIO(azure_edit(cfg, crop, mask, replacement)))
        # The model returns a fixed supported size; map it back onto the crop.
        # VALIDATE: if aspect differs this distorts — prefer cropping to a supported
        # aspect, or request a matching `size`.
        edited = edited.convert("RGB").resize(crop.size)
        img.paste(edited, (rx0, ry0))
        print(f"  [edited] {original!r} -> {replacement!r}")

    img.save(outp)
    print(f"wrote {outp}\nNOW VERIFY: verify.py {outp} --strings <originals.json>, "
          f"then eyeball the render.")


if __name__ == "__main__":
    main()
