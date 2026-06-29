#!/usr/bin/env python3
"""OPTIONAL high-fidelity raster anonymization via an Azure image-edit model.

For scanned / image documents, replaces each PII region by sending a small SQUARE
crop around that region (plus a mask over the value) to an Azure image-edit model
(e.g. gpt-image-1) and compositing the regenerated text back into the original.
The model matches the surrounding font *style* and background far better than the
local cover-and-reinsert path.

It is GENERATIVE, so it is NOT pixel/font-perfect and MUST still be verified:
  - It matches font style, not the exact typeface.
  - It can leave a faint trace of the original or render the wrong value.
  - Numbers are especially prone to garbling.

Two design choices keep the rest of the page byte-identical and undistorted —
both learned the hard way, because the naive crop-and-paste produced black bars
and ghosted/displaced neighbors:

  1. SEND A SQUARE CROP. Image-edit models regenerate the whole canvas at a fixed
     output size (often a square). If you send a thin text strip and squish the
     square result back into it, every glyph is vertically crushed — that is where
     the black bars and smearing came from. A square in maps to a square out, so
     scaling back is a *uniform* shrink with no aspect distortion. The crop is
     grown around the text into real page pixels for context (letterboxed with the
     paper color only when it runs past a page edge).

  2. COMPOSITE BACK ONLY THE MASKED REGION, not the whole returned crop. With a
     mask the model is *supposed* to touch only the transparent region, but in
     practice it subtly redraws the kept pixels too — pasting the whole crop lets
     that drift land on the page (ghosted/doubled neighbors). Writing back just the
     regenerated region confines every change to where we actually intended it.

CONFIG — read ONLY from a gitignored <skill>/.env file (never committed), as
KEY=VALUE lines:
    ANON_AZURE_IMAGE_ENDPOINT=https://<resource>.services.ai.azure.com
    ANON_AZURE_IMAGE_KEY=<resource key>
    ANON_AZURE_IMAGE_DEPLOYMENT=<image-edit deployment name>

PRIVACY: this UPLOADS the un-anonymized crop to Azure. Do not use it for data that
must not leave the machine — the local path keeps everything on-device.

VERIFICATION: always run verify.py on the OUTPUT afterwards (re-OCR: originals
gone AND intended replacements present) and eyeball the render.

Usage:
    redact_image_ai.py INPUT.png OUTPUT.png REPLACEMENTS.json
Exit codes: 0 ok · 2 not configured (caller should fall back to local) · 1 error

NOTE: this is a reference implementation. Validate against your live deployment —
especially that the model returns a SQUARE (the code warns if it doesn't; if yours
returns a fixed non-square size, request a matching `size=` in azure_edit so the
result still maps back uniformly) — before relying on it.
"""
import base64
import csv
import io
import json
import os
import subprocess
import sys

CONTEXT_PAD = 24      # px of real page context grown around the text before squaring
MASK_PAD = 4          # px around the value to regenerate (clears the glyph AA fringe).
                      # Raise it for very high-DPI scans where the fringe is wider; keep
                      # it < NEIGHBOR_GAP so the mask stays clear of an adjacent field.
WIDTH_FUDGE = 1.3     # a replacement renders ~30% wider than the original at equal length
MIN_GROW_LINES = 1.5  # ...but always allow at least ~1.5 line-heights of headroom (~3 char
                      # widths, since char width ~ 0.5*line-height — DPI-invariant)
NEIGHBOR_GAP = 8      # px to leave before an adjacent field when widening (keep > MASK_PAD)
VGROW_FRAC = 0.35     # vertical headroom each side as a fraction of line-height. A mask
                      # tight to the original glyph box clips a taller-rendered replacement
                      # into a horizontal slice; this gives room. Bounded by the rows
                      # above/below, so bigger is safe-ish, but too big lets the model float
                      # the text off the baseline — ~1/3 line stops clipping without that.


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
    }
    return cfg if all(cfg.values()) else None


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


def locate_all(words, target):
    """Tightest union bbox of `target` for EVERY occurrence — one per line it appears
    on (and >1 per line if it repeats there). Returns a list, empty if not found.

    Returning all occurrences, not just the first, is a safety requirement: a PII
    string that repeats (e.g. an invoice number printed in BOTH the header and the
    footer) must be redacted everywhere, or the later occurrences silently leak.

    Each match must HUG the target: a preceding label on the same line (e.g.
    `VIN: <value>`, `Lic #: <value>`, `Phone: <value>`) shares the OCR line, and a
    naive substring match would return a box spanning the label too — then the label
    gets masked and regenerated away. So after finding a window that contains the
    target, trim leading words while the remainder still contains it, leaving only
    the value's box."""
    norm = " ".join(target.split())
    if not norm:                 # an empty/whitespace key is `in` every token —
        return []                # without this it would mask the entire document
    # Caution: this is a SUBSTRING match, and main() now edits EVERY occurrence. A
    # short/numeric key (e.g. "5.00", an account fragment) can match inside an
    # unrelated kept token ("$1,005.00"), and each hit costs an Azure edit that
    # rewrites that token — which verify.py (originals-absent only) won't catch.
    # Tightening to token boundaries is unsafe (it could skip a real value OCR
    # merged with punctuation, a silent leak), so prefer specific keys and do the
    # required visual pass on KEPT content, not just the scrubbed values.
    by_line = {}
    for w in words:
        by_line.setdefault(w[5], []).append(w)
    boxes = []
    for line in by_line.values():
        i, n = 0, len(line)
        while i < n:
            joined, hit = "", None
            for j in range(i, n):
                joined = (joined + " " + line[j][0]).strip()
                if norm in joined:
                    k = i  # drop leading words not needed to still contain `norm`
                    while k < j and norm in " ".join(g[0] for g in line[k + 1:j + 1]):
                        k += 1
                    grp = line[k:j + 1]
                    boxes.append((min(g[1] for g in grp), min(g[2] for g in grp),
                                  max(g[3] for g in grp), max(g[4] for g in grp)))
                    hit = j
                    break
                if len(joined) > len(norm) + 40:
                    break
            i = hit + 1 if hit is not None else i + 1  # resume AFTER a match (>1 per line)
    return boxes


def _line_neighbors(words, box):
    """(left_limit, right_limit): the inner edges of the nearest words flanking
    `box` on the same row, or None where the value ends/starts the row.

    'Same row' = vertical OVERLAP with the value box. Testing overlap (not whether
    a single center-y lands inside a word) catches a neighbor set in a different
    font size, which is exactly the neighbor we must not grow over. Used to bound
    widening so growth only ever consumes whitespace, never an adjacent field."""
    x0, y0, x1, y1 = box
    same_row = [w for w in words if w[0].strip() and w[2] < y1 and w[4] > y0]
    rights = [w[1] for w in same_row if w[1] >= x1]
    lefts = [w[3] for w in same_row if w[3] <= x0]
    return (max(lefts) if lefts else None, min(rights) if rights else None)


def _col_neighbors(words, box):
    """(top_limit, bottom_limit): the inner edges of the nearest words ABOVE and
    BELOW `box` in the same column, or None where the column ends. The vertical
    analog of `_line_neighbors` — bounds vertical headroom so it only ever consumes
    whitespace, never an adjacent line (which the model would otherwise regenerate
    and the composite would then paste back over).

    'Same column' = horizontal OVERLAP with the value box. A same-row label (e.g.
    `VIN:`) sits left of the value and so does NOT horizontally overlap it — it is
    excluded here and handled by `_line_neighbors` instead."""
    x0, y0, x1, y1 = box
    same_col = [w for w in words if w[0].strip() and w[1] < x1 and w[3] > x0]
    above = [w[4] for w in same_col if w[4] <= y0]   # bottom edges of words above
    below = [w[2] for w in same_col if w[2] >= y1]   # top edges of words below
    return (max(above) if above else None, min(below) if below else None)


def edit_box(words, box, original, replacement, img_size):
    """Grow the tight value box so a wider OR taller replacement isn't clipped.

    HORIZONTAL: a replacement usually renders wider than the original (more/wider
    glyphs, or the model drawing slightly larger), overflowing the mask and clipping
    its end glyph(s). Grow by the expected extra width (length-ratio scaled, with a
    line-height floor), bounded by the flanking words so growth only consumes
    whitespace. Left-aligned values overflow right, so grow right first; spill left
    only when the right is blocked (right-aligned values / tight columns).

    VERTICAL: the model often renders the replacement a touch taller or shifted, and
    a mask tight to the original glyph box (tesseract boxes hug the ink) slices the
    top/bottom off into a clean horizontal cut. Grow up and down by VGROW_FRAC of the
    line-height, bounded by the rows above/below so the mask never reaches a neighbor
    line."""
    W, H = img_size
    x0, y0, x1, y1 = box
    h = y1 - y0
    ratio = len(replacement.strip()) / max(1, len(original.strip()))
    want = (x1 - x0) * max(1.0, ratio) * WIDTH_FUDGE
    extra = max(int(want) - (x1 - x0), int(h * MIN_GROW_LINES))
    left_lim, right_lim = _line_neighbors(words, box)
    avail_r = max(0, (right_lim - x1 - NEIGHBOR_GAP) if right_lim is not None else (W - x1))
    avail_l = max(0, (x0 - left_lim - NEIGHBOR_GAP) if left_lim is not None else x0)
    grow_r = min(extra, avail_r)
    grow_l = min(extra - grow_r, avail_l)            # spill left only if right is blocked
    # Bound vertical growth against words above/below across the ALREADY-WIDENED span
    # (x0-grow_l .. x1+grow_r), not the tight value column — otherwise a field on the
    # line above/below sitting in the widened flank is invisible to the bound and the
    # (also taller) mask clips a fragment of it.
    top_lim, bot_lim = _col_neighbors(words, (x0 - grow_l, y0, x1 + grow_r, y1))
    vextra = int(h * VGROW_FRAC)
    avail_t = max(0, (y0 - top_lim - NEIGHBOR_GAP) if top_lim is not None else y0)
    avail_b = max(0, (bot_lim - y1 - NEIGHBOR_GAP) if bot_lim is not None else (H - y1))
    grow_t = min(vextra, avail_t)
    grow_b = min(vextra, avail_b)
    return (x0 - grow_l, y0 - grow_t, x1 + grow_r, y1 + grow_b)


def make_mask(size, box):
    """Opaque (keep) everywhere; transparent (edit) over `box` — OpenAI mask
    semantics: alpha==0 marks the region to regenerate. `box` is the final,
    already-padded edit region in crop-local coordinates (the caller is the single
    source of truth for the padding, so the mask and the composited patch can't
    diverge). `box` is half-open like PIL's crop (x1/y1 exclusive); rectangle()
    fills inclusively, so draw to x1-1/y1-1 to cover exactly the pasted region."""
    from PIL import Image, ImageDraw
    x0, y0, x1, y1 = box
    m = Image.new("RGBA", size, (0, 0, 0, 255))
    ImageDraw.Draw(m).rectangle((x0, y0, x1 - 1, y1 - 1), fill=(0, 0, 0, 0))
    return m


def _paper_color(im):
    """Median of the crop's four corners — a robust guess at the paper/background
    color, used only to letterbox a square that runs past a page edge."""
    w, h = im.size
    if not (w and h):
        return (255, 255, 255)
    px = im.convert("RGB").load()
    corners = [px[0, 0], px[w - 1, 0], px[0, h - 1], px[w - 1, h - 1]]
    return tuple(int(sum(sorted(c[k] for c in corners)[1:3]) / 2) for k in range(3))


def square_crop(img, box):
    """A SQUARE crop centered on `box`, grown by CONTEXT_PAD for context.

    Square in -> square out -> uniform scale back (no aspect distortion). Grows
    into real page pixels where available; letterboxes with the paper color only
    where the square runs past an edge (a flat synthetic border would be a worse
    context for the model than the page's real pixels, so we keep them where we
    can). Returns (crop_image, (ox, oy)) where (ox, oy) is the square's top-left
    in image coordinates (may be negative)."""
    from PIL import Image
    W, H = img.size
    x0, y0, x1, y1 = box
    side = min(max(x1 - x0, y1 - y0) + 2 * CONTEXT_PAD, max(W, H))
    cx, cy = (x0 + x1) // 2, (y0 + y1) // 2
    ox, oy = cx - side // 2, cy - side // 2            # square top-left (may be <0)

    ix0, iy0 = max(0, ox), max(0, oy)                  # real pixels available...
    ix1, iy1 = min(W, ox + side), min(H, oy + side)    # ...inside the page
    real = img.crop((ix0, iy0, ix1, iy1))
    if (ix1 - ix0, iy1 - iy0) == (side, side):
        return real, (ox, oy)
    crop = Image.new("RGB", (side, side), _paper_color(real))
    crop.paste(real, (ix0 - ox, iy0 - oy))            # letterbox the deficit
    return crop, (ox, oy)


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
    """Edit `crop` (square) with `mask`; return the PNG bytes the model emits."""
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


def _redact_box(cfg, img, words, box, original, replacement):
    """Edit one located occurrence of `original` -> `replacement` in `img`, in place."""
    from PIL import Image
    ex0, ey0, ex1, ey1 = edit_box(words, box, original, replacement, img.size)
    crop, (ox, oy) = square_crop(img, (ex0, ey0, ex1, ey1))

    # The clear region = the grown value box padded by MASK_PAD. Compute it ONCE,
    # clamped to BOTH the page and the crop, so the mask, the composited patch, and
    # the paste target stay identical — and so a capped square at a page edge can
    # never make a crop coord go negative (which PIL would silently fill with black,
    # re-introducing the black-bar artifact).
    mx0, my0 = max(0, ex0 - MASK_PAD), max(0, ey0 - MASK_PAD)
    mx1, my1 = min(img.width, ex1 + MASK_PAD), min(img.height, ey1 + MASK_PAD)
    lx0, ly0 = max(0, mx0 - ox), max(0, my0 - oy)
    lx1, ly1 = min(crop.width, mx1 - ox), min(crop.height, my1 - oy)
    local = (lx0, ly0, lx1, ly1)

    mask = make_mask(crop.size, local)
    edited = Image.open(io.BytesIO(azure_edit(cfg, crop, mask, replacement)))
    if abs(edited.width - edited.height) > 1:
        print(f"  [warn] model returned non-square {edited.size}; resizing to a "
              f"square will distort the result. Pin a square size= in azure_edit "
              f"for this deployment.", file=sys.stderr)
    # Square in -> square out: this is a uniform shrink, NOT an aspect-distorting
    # squish (see the warning above for the non-square case).
    edited = edited.convert("RGB").resize(crop.size)

    # Composite back ONLY the regenerated clear region (same `local`), at its true
    # page position, so the model's incidental drift in the kept context can't land
    # on the page.
    patch = edited.crop(local)
    img.paste(patch, (ox + lx0, oy + ly0))


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
        boxes = locate_all(words, original)
        if not boxes:
            print(f"  [!! NOT LOCATED] {original!r} — OCR didn't find it; handle it "
                  f"manually and re-verify.", file=sys.stderr)
            continue
        for box in boxes:
            _redact_box(cfg, img, words, box, original, replacement)
        n = len(boxes)
        print(f"  [edited] {original!r} -> {replacement!r}" + (f"  ({n}x)" if n > 1 else ""))

    img.save(outp)
    print(f"wrote {outp}\nNOW VERIFY: verify.py {outp} --strings <originals.json>, "
          f"then eyeball the render.")


if __name__ == "__main__":
    main()
