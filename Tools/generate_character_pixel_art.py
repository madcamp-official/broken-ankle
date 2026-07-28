from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Assets" / "Art" / "characters"
CELL = 32
MONSTER_CELL = 64
DIRECTIONS = ("down", "up", "left", "right")


P = {
    "transparent": (0, 0, 0, 0),
    "outline": (16, 17, 21, 255),
    "deep": (31, 32, 39, 255),
    "shadow": (52, 53, 61, 255),
    "skin": (172, 134, 105, 255),
    "skin_dark": (105, 76, 62, 255),
    "cream": (202, 194, 158, 255),
    "orange": (188, 88, 48, 255),
    "orange_dark": (105, 52, 42, 255),
    "teal": (47, 156, 148, 255),
    "teal_dark": (31, 81, 91, 255),
    "blue": (56, 83, 128, 255),
    "blue_dark": (31, 42, 75, 255),
    "yellow": (202, 153, 54, 255),
    "green": (78, 117, 76, 255),
    "green_dark": (39, 65, 49, 255),
    "metal": (112, 119, 124, 255),
    "metal_dark": (62, 68, 72, 255),
    "rubber": (22, 25, 27, 255),
    "bone": (151, 143, 125, 255),
    "sick": (132, 163, 119, 255),
    "speaker": (36, 35, 38, 255),
    "wire_red": (133, 42, 43, 255),
    "blood": (92, 18, 24, 255),
    "blood_hi": (169, 36, 42, 255),
    "flesh": (181, 102, 89, 255),
    "flesh_dark": (104, 49, 54, 255),
    "bruise": (66, 47, 72, 255),
    "teeth": (219, 207, 168, 255),
    "wet": (55, 92, 95, 255),
}


def rect(d, box, color):
    d.rectangle(box, fill=color)


def px(d, x, y, color):
    d.point((x, y), fill=color)


def line(d, coords, color, width=1):
    d.line(coords, fill=color, width=width)


def blank():
    return Image.new("RGBA", (CELL, CELL), P["transparent"])


def blank_monster():
    return Image.new("RGBA", (MONSTER_CELL, MONSTER_CELL), P["transparent"])


def paste(sheet, sprite, frame, row):
    sheet.alpha_composite(sprite, (frame * sprite.width, row * sprite.height))


def leg_phase(frame):
    return [0, 1, 0, -1][frame % 4]


def draw_head(d, direction, x, y, role):
    hair = P["deep"] if role == "audio" else P["blue_dark"]
    hardhat = role == "power"

    if hardhat:
        rect(d, (x - 5, y - 5, x + 5, y - 2), P["yellow"])
        rect(d, (x - 7, y - 2, x + 7, y - 1), P["yellow"])
        rect(d, (x - 4, y - 6, x + 4, y - 5), P["outline"])
    else:
        rect(d, (x - 5, y - 5, x + 5, y + 1), hair)

    rect(d, (x - 5, y - 2, x + 5, y + 5), P["skin"])
    rect(d, (x - 5, y + 4, x + 5, y + 5), P["skin_dark"])

    if direction == "down":
        px(d, x - 3, y + 1, P["outline"])
        px(d, x + 3, y + 1, P["outline"])
        rect(d, (x - 1, y + 4, x + 1, y + 4), P["skin_dark"])
    elif direction == "up":
        rect(d, (x - 5, y - 2, x + 5, y + 4), hair if not hardhat else P["yellow"])
    else:
        face_x = x + (3 if direction == "right" else -3)
        px(d, face_x, y + 1, P["outline"])


def draw_headset(d, direction, x, y, color):
    rect(d, (x - 7, y - 1, x - 6, y + 4), color)
    rect(d, (x + 6, y - 1, x + 7, y + 4), color)
    line(d, (x - 6, y - 5, x + 6, y - 5), color)
    if direction != "up":
        line(d, (x + 6, y + 3, x + 9, y + 6), color)


def draw_player(role, direction, frame):
    img = blank()
    d = ImageDraw.Draw(img)
    bob = 1 if frame in (1, 3) else 0
    step = leg_phase(frame)
    x = 16
    torso_y = 15 + bob

    jacket = P["orange"] if role == "audio" else P["green"]
    jacket_dark = P["orange_dark"] if role == "audio" else P["green_dark"]
    accent = P["teal"] if role == "audio" else P["yellow"]
    pants = P["blue_dark"] if role == "audio" else P["deep"]

    # Shadow/contact pixels.
    rect(d, (9, 29, 23, 30), (0, 0, 0, 70))

    draw_head(d, direction, x, 8 + bob, role)
    draw_headset(d, direction, x, 8 + bob, P["teal_dark"])

    if direction in ("down", "up"):
        rect(d, (10, torso_y, 22, torso_y + 9), P["outline"])
        rect(d, (11, torso_y, 21, torso_y + 8), jacket)
        rect(d, (11, torso_y + 6, 21, torso_y + 8), jacket_dark)
        rect(d, (15, torso_y, 17, torso_y + 8), P["cream"] if role == "audio" else P["metal"])

        if direction == "up":
            rect(d, (9, torso_y + 1, 12, torso_y + 8), P["metal_dark"])
            rect(d, (20, torso_y + 1, 23, torso_y + 8), P["metal_dark"])
        elif role == "audio":
            rect(d, (19, torso_y + 2, 23, torso_y + 7), P["teal_dark"])
            px(d, 21, torso_y + 4, P["teal"])
        else:
            rect(d, (9, torso_y + 2, 12, torso_y + 6), P["metal"])
            line(d, (8, torso_y + 1, 6, torso_y + 6), P["metal"])

        arm_l = 8 - step
        arm_r = 23 + step
        rect(d, (arm_l, torso_y + 2, arm_l + 2, torso_y + 10), jacket_dark)
        rect(d, (arm_r - 2, torso_y + 2, arm_r, torso_y + 10), jacket_dark)
        rect(d, (11, 24, 15, 29 + step), pants)
        rect(d, (17, 24, 21, 29 - step), pants)
        rect(d, (10, 29 + step, 15, 30 + step), P["rubber"])
        rect(d, (17, 29 - step, 22, 30 - step), P["rubber"])
    else:
        facing_right = direction == "right"
        side = 1 if facing_right else -1
        rect(d, (11, torso_y, 21, torso_y + 9), P["outline"])
        rect(d, (12, torso_y, 20, torso_y + 8), jacket)
        rect(d, (12, torso_y + 6, 20, torso_y + 8), jacket_dark)
        front_x = 21 if facing_right else 10
        rect(d, (front_x, torso_y + 2, front_x, torso_y + 7), accent)
        rect(d, (8 if not facing_right else 21, torso_y + 2, 10 if not facing_right else 23, torso_y + 9), jacket_dark)
        if role == "audio":
            rect(d, (8 if facing_right else 21, torso_y + 3, 10 if facing_right else 23, torso_y + 8), P["teal_dark"])
        else:
            line(d, (22 if facing_right else 9, torso_y + 1, 25 if facing_right else 6, torso_y + 5), P["metal"])
        rect(d, (12, 24, 15, 29 + step), pants)
        rect(d, (17, 24, 20, 29 - step), pants)
        rect(d, (11, 29 + step, 16, 30 + step), P["rubber"])
        rect(d, (16, 29 - step, 21, 30 - step), P["rubber"])
        if role == "power":
            rect(d, (13 - side, torso_y + 1, 15 - side, torso_y + 5), P["metal_dark"])

    return img


def draw_speaker(d, cx, cy, broken=False):
    rect(d, (cx - 3, cy - 3, cx + 3, cy + 3), P["outline"])
    rect(d, (cx - 2, cy - 2, cx + 2, cy + 2), P["speaker"])
    px(d, cx, cy, P["metal"])
    if broken:
        line(d, (cx - 2, cy - 2, cx + 2, cy + 2), P["wire_red"])


def draw_big_speaker(d, cx, cy, r=6, broken=False, mouth=False):
    rect(d, (cx - r - 1, cy - r - 1, cx + r + 1, cy + r + 1), P["outline"])
    rect(d, (cx - r, cy - r, cx + r, cy + r), P["speaker"])
    rect(d, (cx - r + 2, cy - r + 2, cx + r - 2, cy + r - 2), P["deep"])
    if mouth:
        rect(d, (cx - r + 3, cy - 1, cx + r - 3, cy + 2), P["rubber"])
        px(d, cx + r - 4, cy + 1, P["sick"])
    else:
        rect(d, (cx - 2, cy - 2, cx + 2, cy + 2), P["metal_dark"])
    if broken:
        line(d, (cx - r + 1, cy - r + 1, cx + r - 1, cy + r - 1), P["wire_red"], 2)
        line(d, (cx - r, cy + 1, cx - r - 5, cy + 7), P["wire_red"], 2)


def draw_meat_strand(d, points, bright=False, width=1):
    color = P["blood_hi"] if bright else P["blood"]
    line(d, points, color, width)
    if len(points) >= 4:
        px(d, points[-2], points[-1], P["flesh"])


def draw_big_manager(direction, frame):
    img = blank_monster()
    d = ImageDraw.Draw(img)
    bob = [0, 2, 0][frame % 3]
    sway = [-2, 0, 2][frame % 3]
    side = 1 if direction == "right" else -1

    rect(d, (14, 58, 52, 62), (0, 0, 0, 90))

    # A crooked exoskeleton cage, wider than the body and too heavy for it.
    line(d, (19 + sway, 20 + bob, 13, 53), P["metal_dark"], 3)
    line(d, (45 + sway, 18 + bob, 52, 55), P["metal_dark"], 3)
    line(d, (18, 42 + bob, 48, 37 + bob), P["metal_dark"], 2)
    rect(d, (13, 45 + bob, 16, 58), P["metal"])
    rect(d, (49, 42 + bob, 52, 58), P["metal"])

    # Back speaker cluster. It should read as equipment first, body second.
    if direction in ("down", "up"):
        draw_big_speaker(d, 15 + sway, 18 + bob, 6, frame == 1, mouth=frame == 2)
        draw_big_speaker(d, 50 + sway, 23 + bob, 7, frame == 2)
        draw_big_speaker(d, 11 + sway, 36 + bob, 5, frame == 0)
        draw_big_speaker(d, 47 + sway, 42 + bob, 5, frame == 1, mouth=True)
    else:
        back_x = 14 if direction == "right" else 50
        draw_big_speaker(d, back_x, 19 + bob, 7, frame == 1)
        draw_big_speaker(d, back_x, 36 + bob, 6, frame == 2, mouth=True)
        draw_big_speaker(d, back_x + side * -4, 49 + bob, 4, frame == 0)

    # Helmet: the gear is intact, but something human is smeared into the inside.
    hx = 32 + sway
    rect(d, (22 + sway, 8 + bob, 43 + sway, 25 + bob), P["outline"])
    rect(d, (24 + sway, 10 + bob, 41 + sway, 23 + bob), P["bone"])
    rect(d, (26 + sway, 15 + bob, 39 + sway, 21 + bob), P["rubber"])
    rect(d, (27 + sway, 16 + bob, 37 + sway, 19 + bob), P["flesh_dark"])
    rect(d, (28 + sway, 16 + bob, 32 + sway, 18 + bob), P["flesh"])
    px(d, 30 + sway, 17 + bob, P["outline"])
    px(d, 35 + sway, 18 + bob, P["blood_hi"])
    rect(d, (31 + sway, 20 + bob, 38 + sway, 21 + bob), P["teeth"])
    px(d, 34 + sway, 21 + bob, P["blood"])
    rect(d, (21 + sway, 13 + bob, 24 + sway, 19 + bob), P["metal_dark"])
    rect(d, (41 + sway, 12 + bob, 45 + sway, 21 + bob), P["metal_dark"])
    draw_meat_strand(d, (36 + sway, 21 + bob, 36 + sway, 26 + bob, 34, 30 + bob), frame == 1)
    if direction == "up":
        rect(d, (25 + sway, 13 + bob, 40 + sway, 22 + bob), P["metal_dark"])
        rect(d, (29 + sway, 16 + bob, 36 + sway, 21 + bob), P["bruise"])
        draw_big_speaker(d, hx, 17 + bob, 4, frame == 0, mouth=True)
    elif direction == "left":
        rect(d, (23 + sway, 15 + bob, 30 + sway, 20 + bob), P["flesh_dark"])
        px(d, 24 + sway, 18 + bob, P["teeth"])
    elif direction == "right":
        rect(d, (35 + sway, 15 + bob, 43 + sway, 20 + bob), P["flesh_dark"])
        px(d, 42 + sway, 18 + bob, P["teeth"])

    # Suit body: torn absorber padding with exposed human remains strapped inside.
    rect(d, (20, 26 + bob, 44, 48 + bob), P["outline"])
    rect(d, (22, 27 + bob, 42, 47 + bob), P["rubber"])
    rect(d, (24, 29 + bob, 35, 47 + bob), P["bone"])
    rect(d, (30, 30 + bob, 39, 46 + bob), P["flesh_dark"])
    rect(d, (31, 31 + bob, 37, 44 + bob), P["flesh"])
    rect(d, (26, 33 + bob, 29, 35 + bob), P["teeth"])
    rect(d, (26, 38 + bob, 29, 40 + bob), P["teeth"])
    rect(d, (26, 43 + bob, 29, 45 + bob), P["teeth"])
    rect(d, (36, 31 + bob, 41, 46 + bob), P["sick"])
    rect(d, (19, 31 + bob, 24, 43 + bob), P["deep"])
    rect(d, (42, 28 + bob, 47, 44 + bob), P["deep"])
    rect(d, (27, 30 + bob, 29, 45 + bob), P["metal_dark"])
    rect(d, (33, 31 + bob, 35, 48 + bob), P["metal_dark"])
    rect(d, (34, 35 + bob, 41, 39 + bob), P["blood"])
    px(d, 31, 38 + bob, P["blood_hi"])
    px(d, 40, 45 + bob, P["blood_hi"])

    # Cables and anatomy are hard to distinguish now.
    line(d, (23, 29 + bob, 12 + sway, 35 + bob, 18, 51), P["blood"], 2)
    line(d, (40, 28 + bob, 54 + sway, 34 + bob, 48, 55), P["wet"], 2)
    line(d, (29, 25 + bob, 24 + sway, 18 + bob, 17, 19), P["blood_hi"], 1)
    line(d, (38, 24 + bob, 47 + sway, 23 + bob, 52, 29), P["metal"], 1)
    draw_meat_strand(d, (36, 43 + bob, 34 + sway, 51, 32, 57), frame == 2, 2)
    draw_meat_strand(d, (41, 37 + bob, 47 + sway, 43, 51, 51), frame == 0, 1)
    draw_meat_strand(d, (27, 42 + bob, 22 + sway, 48, 18, 56), frame == 1, 1)
    for i, (x, y) in enumerate(((25, 36), (43, 38), (30, 47), (22, 44), (39, 29))):
        px(d, x + (frame + i) % 2, y + bob, P["flesh"] if i % 2 else P["blood_hi"])

    # Asymmetrical arms. One is still partly an arm; the other is a clamp-like silencing tool.
    arm_drop = [0, 2, -1][frame % 3]
    rect(d, (12 + sway, 28 + bob, 19 + sway, 53 + bob + arm_drop), P["outline"])
    rect(d, (14 + sway, 30 + bob, 18 + sway, 42 + bob + arm_drop), P["bone"])
    rect(d, (14 + sway, 42 + bob + arm_drop, 18 + sway, 51 + bob + arm_drop), P["flesh_dark"])
    rect(d, (13 + sway, 48 + bob + arm_drop, 20 + sway, 55 + bob + arm_drop), P["blood"])
    rect(d, (12 + sway, 55 + bob + arm_drop, 16 + sway, 58 + bob + arm_drop), P["flesh"])
    px(d, 18 + sway, 55 + bob + arm_drop, P["teeth"])
    rect(d, (45 + sway, 26 + bob, 52 + sway, 55 + bob - arm_drop), P["outline"])
    rect(d, (47 + sway, 28 + bob, 50 + sway, 52 + bob - arm_drop), P["metal"])
    rect(d, (48 + sway, 51 + bob - arm_drop, 56 + sway, 57 + bob - arm_drop), P["rubber"])
    rect(d, (53 + sway, 49 + bob - arm_drop, 57 + sway, 52 + bob - arm_drop), P["metal_dark"])
    draw_meat_strand(d, (47 + sway, 33 + bob, 43, 40 + bob, 44, 48 + bob), frame == 1)

    # Long braced legs, with one ruined human leg visible between the struts.
    step = [-1, 1, 0][frame % 3]
    rect(d, (23, 47 + bob, 29, 60 + step), P["outline"])
    rect(d, (25, 48 + bob, 27, 58 + step), P["metal"])
    rect(d, (35, 46 + bob, 42, 60 - step), P["outline"])
    rect(d, (37, 48 + bob, 40, 54 - step), P["metal_dark"])
    rect(d, (38, 54 - step, 41, 59 - step), P["flesh_dark"])
    rect(d, (19, 58 + step, 31, 62 + step), P["rubber"])
    rect(d, (34, 58 - step, 48, 62 - step), P["rubber"])
    line(d, (25, 50 + bob, 21, 59 + step), P["metal_dark"], 2)
    line(d, (39, 49 + bob, 45, 59 - step), P["metal_dark"], 2)

    # Direction-specific silhouette tweaks.
    if direction == "left":
        rect(d, (18 + sway, 16 + bob, 25 + sway, 22 + bob), P["flesh_dark"])
        line(d, (21 + sway, 22 + bob, 20, 29 + bob), P["blood_hi"], 1)
        rect(d, (46, 30 + bob, 51, 47 + bob), P["deep"])
    elif direction == "right":
        rect(d, (39 + sway, 16 + bob, 46 + sway, 22 + bob), P["flesh_dark"])
        line(d, (43 + sway, 22 + bob, 44, 29 + bob), P["blood_hi"], 1)
        rect(d, (13, 30 + bob, 18, 47 + bob), P["deep"])
    elif direction == "down":
        rect(d, (30 + sway, 20 + bob, 35 + sway, 22 + bob), P["blood"])
        line(d, (31 + sway, 22 + bob, 30, 28 + bob), P["blood_hi"], 1)

    return img


def draw_manager(direction, frame):
    img = blank()
    d = ImageDraw.Draw(img)
    bob = [0, 1, 0][frame % 3]
    sway = [-1, 0, 1][frame % 3]

    rect(d, (6, 29, 26, 31), (0, 0, 0, 85))

    # Back-mounted sound array and bent support frame.
    if direction in ("down", "up"):
        draw_speaker(d, 9 + sway, 10 + bob, frame == 1)
        draw_speaker(d, 23 + sway, 13 + bob, frame == 2)
        draw_speaker(d, 8 + sway, 20 + bob, frame == 2)
        line(d, (10, 13 + bob, 7, 25), P["metal_dark"], 2)
        line(d, (22, 15 + bob, 25, 27), P["metal_dark"], 2)
    else:
        back_x = 8 if direction == "right" else 24
        draw_speaker(d, back_x, 12 + bob, frame == 1)
        draw_speaker(d, back_x, 20 + bob, frame == 2)
        line(d, (back_x, 15, 16, 24), P["metal_dark"], 2)

    # Suit body: too tall, lopsided, rubberized.
    rect(d, (11 + sway, 5 + bob, 21 + sway, 13 + bob), P["outline"])
    rect(d, (12 + sway, 6 + bob, 20 + sway, 12 + bob), P["bone"])
    rect(d, (13 + sway, 9 + bob, 19 + sway, 10 + bob), P["rubber"])

    if direction != "up":
        rect(d, (15 + sway, 8 + bob, 19 + sway, 9 + bob), P["wet"])
        px(d, 18 + sway, 9 + bob, P["sick"])
    else:
        rect(d, (12 + sway, 7 + bob, 20 + sway, 11 + bob), P["metal_dark"])

    rect(d, (10, 14 + bob, 22, 24 + bob), P["outline"])
    rect(d, (11, 14 + bob, 21, 23 + bob), P["metal_dark"])
    rect(d, (12, 15 + bob, 20, 23 + bob), P["rubber"])
    rect(d, (13, 16 + bob, 17, 23 + bob), P["bone"])
    rect(d, (18, 17 + bob, 20, 23 + bob), P["sick"])

    # Absorber foam, cables, and wet seams.
    rect(d, (9, 16 + bob, 11, 21 + bob), P["deep"])
    rect(d, (21, 15 + bob, 24, 22 + bob), P["deep"])
    line(d, (12, 16 + bob, 7, 18 + bob, 10, 25), P["wire_red"])
    line(d, (20, 15 + bob, 25, 17 + bob, 23, 26), P["wet"])
    px(d, 14, 20 + bob, P["sick"])
    px(d, 21, 22 + bob, P["wire_red"])

    # Arms hang like tools, not limbs.
    arm_drop = 1 if frame == 2 else 0
    rect(d, (6 + sway, 15 + bob, 9 + sway, 25 + bob + arm_drop), P["outline"])
    rect(d, (7 + sway, 16 + bob, 8 + sway, 24 + bob + arm_drop), P["bone"])
    rect(d, (23 + sway, 14 + bob, 26 + sway, 27 + bob - arm_drop), P["outline"])
    rect(d, (24 + sway, 15 + bob, 25 + sway, 26 + bob - arm_drop), P["metal"])
    rect(d, (25 + sway, 24 + bob - arm_drop, 27 + sway, 27 + bob - arm_drop), P["rubber"])

    # Exoskeleton legs.
    rect(d, (11, 24 + bob, 14, 30), P["outline"])
    rect(d, (12, 24 + bob, 13, 29), P["metal"])
    rect(d, (18, 23 + bob, 22, 30), P["outline"])
    rect(d, (19, 24 + bob, 21, 29), P["metal_dark"])
    rect(d, (9, 29, 15, 31), P["rubber"])
    rect(d, (18, 29, 25, 31), P["rubber"])

    # Directional hints.
    if direction == "left":
        rect(d, (11 + sway, 8 + bob, 14 + sway, 9 + bob), P["wet"])
    elif direction == "right":
        rect(d, (18 + sway, 8 + bob, 21 + sway, 9 + bob), P["wet"])
    elif direction == "up":
        draw_speaker(d, 16, 9 + bob, frame == 0)

    return img


def build_player_sheet(role, filename):
    sheet = Image.new("RGBA", (CELL * 4, CELL * 4), P["transparent"])
    for row, direction in enumerate(DIRECTIONS):
        for frame in range(4):
            paste(sheet, draw_player(role, direction, frame), frame, row)
    sheet.save(OUT / filename)
    return sheet


def build_manager_sheet():
    sheet = Image.new("RGBA", (CELL * 3, CELL * 4), P["transparent"])
    for row, direction in enumerate(DIRECTIONS):
        for frame in range(3):
            paste(sheet, draw_manager(direction, frame), frame, row)
    sheet.save(OUT / "quiet_manager_32x32_4dir_3f.png")
    return sheet


def build_big_manager_sheet():
    sheet = Image.new("RGBA", (MONSTER_CELL * 3, MONSTER_CELL * 4), P["transparent"])
    for row, direction in enumerate(DIRECTIONS):
        for frame in range(3):
            paste(sheet, draw_big_manager(direction, frame), frame, row)
    sheet.save(OUT / "quiet_manager_64x64_4dir_3f.png")
    return sheet


def build_preview(images):
    scale = 6
    gap = 12
    labels = [
        "player_audio_technician",
        "player_power_technician",
        "quiet_manager_32",
        "quiet_manager_64",
    ]
    width = max(img.width for img in images) * scale
    height = sum(img.height * scale for img in images) + gap * (len(images) - 1)
    preview = Image.new("RGBA", (width, height), (24, 24, 28, 255))
    y = 0
    for img, _label in zip(images, labels):
        scaled = img.resize((img.width * scale, img.height * scale), Image.Resampling.NEAREST)
        preview.alpha_composite(scaled, (0, y))
        y += scaled.height + gap
    preview.save(OUT / "characters_preview_x6.png")


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    sheets = [
        build_player_sheet("audio", "player_audio_technician_32x32_4dir_4f.png"),
        build_player_sheet("power", "player_power_technician_32x32_4dir_4f.png"),
        build_manager_sheet(),
        build_big_manager_sheet(),
    ]
    build_preview(sheets)
    print(f"Wrote character sheets to {OUT}")


if __name__ == "__main__":
    main()
