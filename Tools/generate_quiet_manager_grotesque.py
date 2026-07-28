from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Assets" / "Art" / "Characters"
TARGET = OUT / "quiet_manager_64x64_4dir_3f.png"
ORIGINAL_BACKUP = OUT / "quiet_manager_64x64_4dir_3f_original_backup.png"
CLUTTERED_BACKUP = OUT / "quiet_manager_64x64_4dir_3f_cluttered_backup.png"
PREVIEW = OUT / "quiet_manager_64x64_4dir_3f_preview_x6.png"

CELL = 64
DIRECTIONS = ("down", "up", "left", "right")

P = {
    "clear": (0, 0, 0, 0),
    "outline": (13, 12, 15, 255),
    "black": (22, 22, 25, 255),
    "rubber": (35, 32, 35, 255),
    "cloth": (84, 79, 68, 255),
    "cloth_hi": (146, 136, 108, 255),
    "flesh": (176, 88, 78, 255),
    "flesh_hi": (218, 125, 101, 255),
    "flesh_dark": (95, 36, 43, 255),
    "blood": (78, 11, 18, 255),
    "blood_hi": (157, 26, 32, 255),
    "bruise": (58, 42, 66, 255),
    "bone": (220, 202, 153, 255),
    "tooth": (239, 222, 173, 255),
    "sick": (119, 152, 96, 255),
    "metal": (93, 102, 104, 255),
    "metal_dark": (47, 53, 56, 255),
    "speaker": (29, 31, 35, 255),
    "speaker_hi": (65, 72, 74, 255),
}


def rect(d, box, color):
    d.rectangle(box, fill=color)


def px(d, x, y, color):
    d.point((x, y), fill=color)


def line(d, coords, color, width=1):
    d.line(coords, fill=color, width=width)


def poly(d, points, color):
    d.polygon(points, fill=color)


def speaker(d, cx, cy, r=5, broken=False):
    rect(d, (cx - r - 1, cy - r - 1, cx + r + 1, cy + r + 1), P["outline"])
    rect(d, (cx - r, cy - r, cx + r, cy + r), P["speaker"])
    rect(d, (cx - r + 2, cy - r + 2, cx + r - 2, cy + r - 2), P["black"])
    rect(d, (cx - 2, cy - 1, cx + 2, cy + 1), P["speaker_hi"])
    if broken:
        line(d, (cx - r, cy - r, cx + r, cy + r), P["blood_hi"], 2)


def teeth(d, x, y, count, down=True):
    for i in range(count):
        tx = x + i * 4
        if down:
            poly(d, ((tx, y), (tx + 1, y + 4), (tx + 3, y)), P["tooth"])
        else:
            poly(d, ((tx, y + 4), (tx + 1, y), (tx + 3, y + 4)), P["tooth"])


def meat_line(d, coords, bright=False, width=2):
    line(d, coords, P["blood_hi"] if bright else P["blood"], width)
    rect(d, (coords[-2] - 1, coords[-1] - 1, coords[-2] + 1, coords[-1] + 1), P["flesh_hi"])


def body_front(d, bob):
    poly(d, ((21, 25 + bob), (32, 21 + bob), (44, 26 + bob), (48, 47), (40, 58), (24, 58), (16, 46)), P["outline"])
    poly(d, ((23, 27 + bob), (32, 24 + bob), (41, 28 + bob), (44, 46), (38, 54), (26, 54), (19, 45)), P["flesh_dark"])
    rect(d, (25, 29 + bob, 39, 51), P["flesh"])
    rect(d, (34, 35 + bob, 43, 43 + bob), P["blood_hi"])
    rect(d, (25, 31 + bob, 28, 51), P["cloth_hi"])
    rect(d, (30, 31 + bob, 32, 53), P["bone"])
    rect(d, (36, 32 + bob, 38, 53), P["bone"])
    for y in (35, 40, 45):
        line(d, (27, y + bob, 38, y + bob), P["bone"], 1)
    rect(d, (40, 36 + bob, 42, 38 + bob), P["sick"])


def head_front(d, bob, sway, frame):
    poly(d, ((21 + sway, 10 + bob), (39 + sway, 7 + bob), (47 + sway, 15 + bob), (42 + sway, 28 + bob), (25 + sway, 29 + bob), (17 + sway, 19 + bob)), P["outline"])
    poly(d, ((24 + sway, 11 + bob), (39 + sway, 10 + bob), (44 + sway, 16 + bob), (39 + sway, 25 + bob), (26 + sway, 26 + bob), (20 + sway, 19 + bob)), P["cloth_hi"])
    rect(d, (25 + sway, 17 + bob, 41 + sway, 24 + bob), P["flesh_dark"])
    rect(d, (27 + sway, 17 + bob, 35 + sway, 21 + bob), P["flesh"])
    rect(d, (35 + sway, 19 + bob, 42 + sway, 24 + bob), P["blood"])
    px(d, 30 + sway, 19 + bob, P["outline"])
    px(d, 38 + sway, 20 + bob, P["blood_hi"])
    teeth(d, 26 + sway, 24 + bob, 4, down=True)
    teeth(d, 28 + sway, 27 + bob, 3, down=False)
    meat_line(d, (37 + sway, 25 + bob, 35, 32 + bob), frame == 1, 1)


def body_back(d, bob):
    poly(d, ((20, 25 + bob), (32, 20 + bob), (45, 26 + bob), (49, 47), (41, 58), (23, 58), (16, 45)), P["outline"])
    rect(d, (21, 28 + bob, 43, 52), P["rubber"])
    rect(d, (25, 29 + bob, 39, 50), P["cloth"])
    rect(d, (31, 28 + bob, 33, 55), P["bone"])
    rect(d, (29, 33 + bob, 35, 36 + bob), P["flesh_dark"])
    rect(d, (29, 42 + bob, 35, 45 + bob), P["flesh_dark"])
    speaker(d, 20, 33 + bob, 5, broken=True)
    speaker(d, 45, 34 + bob, 6, broken=False)
    speaker(d, 42, 48 + bob, 4, broken=True)


def head_back(d, bob, sway, frame):
    poly(d, ((22 + sway, 9 + bob), (42 + sway, 8 + bob), (47 + sway, 17 + bob), (40 + sway, 27 + bob), (24 + sway, 27 + bob), (17 + sway, 18 + bob)), P["outline"])
    poly(d, ((24 + sway, 11 + bob), (40 + sway, 10 + bob), (44 + sway, 17 + bob), (38 + sway, 24 + bob), (26 + sway, 24 + bob), (20 + sway, 18 + bob)), P["cloth_hi"])
    rect(d, (26 + sway, 15 + bob, 39 + sway, 22 + bob), P["rubber"])
    speaker(d, 32 + sway, 18 + bob, 4, broken=frame == 1)


def body_side(d, direction, bob):
    face_left = direction == "left"
    sign = -1 if face_left else 1
    front_x = 20 if face_left else 44
    back_x = 47 if face_left else 17

    poly(d, ((22, 25 + bob), (34, 22 + bob), (44, 30 + bob), (44, 50), (37, 58), (24, 56), (18, 43)), P["outline"])
    poly(d, ((24, 28 + bob), (34, 25 + bob), (41, 31 + bob), (40, 48), (35, 53), (26, 52), (21, 42)), P["flesh_dark"])
    rect(d, (27, 31 + bob, 39, 49), P["flesh"])
    rect(d, (34, 37 + bob, 42, 44 + bob), P["blood_hi"])
    rect(d, (28, 32 + bob, 30, 51), P["bone"])
    line(d, (30, 36 + bob, 38, 36 + bob), P["bone"], 1)
    line(d, (30, 42 + bob, 39, 42 + bob), P["bone"], 1)
    speaker(d, back_x, 31 + bob, 5, broken=True)
    speaker(d, back_x, 46 + bob, 4, broken=False)
    meat_line(d, (front_x, 43 + bob, front_x + sign * 7, 53, front_x + sign * 10, 60), True, 2)


def head_side(d, direction, bob, sway, frame):
    face_left = direction == "left"
    sign = -1 if face_left else 1
    hx = 31 + sway
    snout = 14 if face_left else 50

    poly(d, ((22 + sway, 10 + bob), (40 + sway, 9 + bob), (45 + sway, 17 + bob), (39 + sway, 27 + bob), (24 + sway, 26 + bob), (18 + sway, 18 + bob)), P["outline"])
    poly(d, ((24 + sway, 12 + bob), (38 + sway, 11 + bob), (42 + sway, 17 + bob), (37 + sway, 24 + bob), (25 + sway, 24 + bob), (21 + sway, 18 + bob)), P["cloth_hi"])
    poly(d, ((hx + sign * 1, 17 + bob), (snout, 20 + bob), (hx + sign * 1, 24 + bob)), P["flesh_dark"])
    rect(d, (min(hx, snout), 19 + bob, max(hx, snout), 22 + bob), P["blood"])
    px(d, hx + sign * 3, 18 + bob, P["outline"])
    if face_left:
        teeth(d, 15, 21 + bob, 4, down=True)
    else:
        teeth(d, 36, 21 + bob, 4, down=True)
    meat_line(d, (hx + sign * 3, 24 + bob, hx + sign * 6, 31 + bob), frame == 2, 1)


def limbs(d, direction, frame, bob):
    step = (-1, 1, 0)[frame]

    # Keep limbs low and readable so they do not compete with the direction cue.
    poly(d, ((15, 32 + bob), (10, 40 + bob), (10, 54), (16, 58), (20, 43 + bob)), P["outline"])
    poly(d, ((16, 34 + bob), (13, 41 + bob), (13, 52), (16, 55), (18, 43 + bob)), P["flesh_dark"])
    rect(d, (11, 51, 18, 58), P["blood"])
    rect(d, (12, 56, 16, 61), P["flesh_hi"])

    poly(d, ((45, 31 + bob), (52, 38 + bob), (54, 53), (50, 58), (46, 45 + bob)), P["outline"])
    poly(d, ((47, 34 + bob), (51, 39 + bob), (52, 50), (49, 54), (47, 44 + bob)), P["flesh"])
    rect(d, (49, 50, 55, 56), P["blood"])

    rect(d, (23, 50 + bob, 29, 61 + step), P["outline"])
    rect(d, (25, 51 + bob, 27, 59 + step), P["metal"])
    poly(d, ((35, 49 + bob), (42, 51 + bob), (43, 61 - step), (36, 61 - step)), P["outline"])
    poly(d, ((37, 51 + bob), (40, 52 + bob), (41, 59 - step), (37, 59 - step)), P["flesh_dark"])
    rect(d, (20, 59 + step, 31, 63 + step), P["rubber"])
    rect(d, (34, 59 - step, 48, 63 - step), P["blood"])


def draw_frame(direction, frame):
    img = Image.new("RGBA", (CELL, CELL), P["clear"])
    d = ImageDraw.Draw(img)
    bob = (0, 2, 0)[frame]
    sway = (-1, 0, 1)[frame]

    rect(d, (11, 59, 53, 63), (0, 0, 0, 85))

    if direction == "down":
        body_front(d, bob)
        head_front(d, bob, sway, frame)
    elif direction == "up":
        body_back(d, bob)
        head_back(d, bob, sway, frame)
    else:
        body_side(d, direction, bob)
        head_side(d, direction, bob, sway, frame)

    limbs(d, direction, frame, bob)

    for i, (x, y) in enumerate(((31, 35), (39, 47), (24, 48), (46, 37))):
        px(d, x + ((frame + i) % 2), y + bob // 2, P["blood_hi"] if i % 2 else P["flesh_hi"])

    return img


def build_sheet():
    if TARGET.exists() and not ORIGINAL_BACKUP.exists():
        ORIGINAL_BACKUP.write_bytes(TARGET.read_bytes())
    if TARGET.exists() and not CLUTTERED_BACKUP.exists():
        CLUTTERED_BACKUP.write_bytes(TARGET.read_bytes())

    sheet = Image.new("RGBA", (CELL * 3, CELL * 4), P["clear"])
    for row, direction in enumerate(DIRECTIONS):
        for frame in range(3):
            sheet.alpha_composite(draw_frame(direction, frame), (frame * CELL, row * CELL))
    sheet.save(TARGET)

    preview = Image.new("RGBA", (sheet.width * 6, sheet.height * 6), (24, 24, 28, 255))
    preview.alpha_composite(sheet.resize(preview.size, Image.Resampling.NEAREST))
    preview.save(PREVIEW)
    print(TARGET)
    print(PREVIEW)


if __name__ == "__main__":
    build_sheet()
