#!/usr/bin/env python3
"""Build the reviewed portrait-frame catalog from installed portrait packs.

The runtime catalog is keyed by ``(frame index, canonical pixel hash)``.  A hash
alone is not sufficient: the same pixels can occur in several slots, while the
meaning of a slot is defined by Stardew's numeric portrait index.  The optional
inventory output keeps every source tile (including unusable and unreviewed
tiles) for audit without making the game parse a multi-megabyte diagnostic file
on every launch.
"""

from __future__ import annotations

import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import struct
import unicodedata

from PIL import Image

from portrait_frame_extra_semantics_vanilla import (
    EXTRA_FRAME_ASSET_OVERRIDES as VANILLA_ASSET_OVERRIDES,
    EXTRA_FRAME_SEMANTICS as VANILLA_EXTRA_SEMANTICS,
)
from portrait_frame_extra_semantics_sve import (
    EXTRA_FRAME_ASSET_OVERRIDES as SVE_ASSET_OVERRIDES,
    EXTRA_FRAME_SEMANTICS as SVE_EXTRA_SEMANTICS,
)


TILE_SIZE = 64
STANDARD_FRAME_COUNT = 6
EXPECTED_SOURCE_PNG_COUNTS: dict[str, int] = {
    "vanilla": 102,
    "oho": 106,
    "sve-seasonal": 162,
    "sve-default": 91,
    "sve-addon": 6,
    "rasmodia": 39,
}
EXPECTED_SOURCE_FINGERPRINTS: dict[str, str] = {
    "vanilla": "FFFB8F5D6E67FEC8BDACA3F40DE161D424B4418A5D639E612FDAB4A3157CF261",
    "oho": "903EBF697C6713DE4D79C9F3B8A4D9EFB5B2D057B88739835F4481AE4358C505",
    "sve-seasonal": "80E8ABF36EA187C084CF10E3FBC5777E8D2CA425165424AE243F10F052FB44C3",
    "sve-default": "847809AC02F345A3D6F5FCB4DC984B455E7EE7F6B32D4307FC5836F03709E004",
    "sve-addon": "DF384969507EF664A8F51CD16AA5F9CA2D0EB72FE37583D0C80D90BC5D057C1E",
    "rasmodia": "99BA9455EF3C07DD5BAA2D0D502E17683F931B93D80932CBB9905C27DAB5B14B",
}
STANDARD_FRAME_SPECS: dict[int, dict[str, object]] = {
    0: {
        "marker": "0",
        "enabled": True,
        "en": "a neutral or baseline expression",
        "zh": "中性或基础神情",
        "review": "standard-slot-convention",
    },
    1: {
        "marker": "h",
        "enabled": True,
        "en": "a clearly happy expression",
        "zh": "明确高兴的神情",
        "review": "standard-slot-convention",
    },
    2: {
        "marker": "s",
        "enabled": True,
        "en": "a sad or downcast expression",
        "zh": "难过或低落的神情",
        "review": "standard-slot-convention",
    },
    3: {
        "marker": "u",
        "enabled": False,
        "en": "an asset-specific unique expression",
        "zh": "该肖像资源特有的表情",
        "review": "manual-per-source",
    },
    4: {
        "marker": "l",
        "enabled": True,
        "en": "love, affection, or a warm fond expression",
        "zh": "爱意、亲昵或温暖的神情",
        "review": "standard-slot-convention",
    },
    5: {
        "marker": "a",
        "enabled": True,
        "en": "clear anger, irritation, or an unmistakable confrontational look",
        "zh": "明确的生气、恼怒或正面冲突神情",
        "review": "standard-slot-convention",
    },
}


# `enabled` controls whether the frame is taught to the model.  Negative and angry
# expressions stay enabled when their meaning is clear; prompt rules decide when
# they are appropriate.  Only unusable, redundant, or genuinely ambiguous art is
# disabled here.
PROFILES: dict[str, dict[str, object]] = {
    "Abigail": {"enabled": True, "en": "a shy, slightly pouty look", "zh": "害羞而略带撅嘴的神情"},
    "Alex": {"enabled": True, "en": "holding a gridball with determined focus", "zh": "抱着格球、专注而坚定"},
    "Caroline": {"enabled": True, "en": "a guarded side glance with mild displeasure", "zh": "戒备而略显不悦的侧目"},
    "Clint": {"enabled": True, "en": "a furrowed, restrained-angry grimace", "zh": "皱眉并压抑怒意的表情"},
    "Demetrius": {"enabled": True, "en": "a concerned, skeptical side glance", "zh": "担忧而狐疑的侧目"},
    "Dwarf": {"enabled": False, "en": "no usable frame 3 in the installed portrait", "zh": "当前肖像没有可用的第 3 帧", "reason": "portrait sheet is only one 64x64 tile"},
    "Elliott": {"enabled": True, "en": "a warm, quietly confident smile", "zh": "温和而自信的微笑"},
    "Emily": {"enabled": True, "en": "a calm, soft expression", "zh": "平静而柔和的神情"},
    "Evelyn": {"enabled": True, "en": "an eyes-closed, reserved, softly blushing look", "zh": "闭眼、含蓄而略微脸红的神情"},
    "George": {"enabled": True, "en": "a pronounced scowl", "zh": "明显皱眉的不高兴表情"},
    "Gus": {"enabled": True, "en": "an eyes-closed, broad content smile", "zh": "闭眼而满足的开怀微笑"},
    "Haley": {"enabled": True, "en": "a soft, quiet, thoughtful look", "zh": "柔和、安静而若有所思的神情"},
    "Harvey": {"enabled": True, "en": "a reserved, tense side glance", "zh": "克制而紧绷的侧目"},
    "Jas": {"enabled": True, "en": "a worried, slightly teary childlike look", "zh": "担心而略带委屈的稚气神情"},
    "Jodi": {"enabled": True, "en": "a straight-mouthed, mildly displeased look", "zh": "嘴角平直而略显不悦的神情"},
    "Kent": {"enabled": True, "en": "an eyes-shut, teeth-clenched grimace", "zh": "闭眼咬牙的强烈烦躁或痛苦"},
    "Krobus": {"enabled": True, "en": "wide-eyed surprise and uncertainty", "zh": "睁大眼睛的惊讶与不安"},
    "Leah": {"enabled": True, "en": "a bashful, eyes-closed smile", "zh": "害羞而闭眼的微笑"},
    "Leo": {"enabled": True, "en": "a gentle, slightly shy smile", "zh": "温和而略显害羞的微笑"},
    "Lewis": {"enabled": True, "en": "a calm, slightly stern but friendly look", "zh": "平静、略严肃但友好的神情"},
    "Linus": {"enabled": True, "en": "a warm, thoughtful look", "zh": "温暖而若有所思的神情"},
    "Marnie": {"enabled": True, "en": "a soft, quietly concerned look", "zh": "柔和而略带担心的神情"},
    "Maru": {"enabled": True, "en": "a playful smile with a finger touching her lips", "zh": "用手指碰唇、俏皮的微笑"},
    "Pam": {"enabled": True, "en": "an open-mouthed angry outburst", "zh": "张嘴怒喊并带怒气符号"},
    "Penny": {"enabled": True, "en": "a calm, gentle smile", "zh": "平静而温柔的微笑"},
    "Pierre": {"enabled": True, "en": "a tight-lipped, displeased side glance", "zh": "紧抿嘴、斜视而不悦"},
    "Robin": {"enabled": True, "en": "a pronounced frown and dissatisfied side glance", "zh": "明显皱眉并不满地侧视"},
    "Sam": {"enabled": True, "en": "an eyes-closed, relaxed happy smile", "zh": "闭眼而放松开心的微笑"},
    "Sandy": {"enabled": True, "en": "a playful wink and smile", "zh": "俏皮地眨眼微笑"},
    "Sebastian": {"enabled": True, "en": "a reserved, faintly amused side glance", "zh": "克制而略带玩味的侧目"},
    "Shane": {"enabled": True, "en": "a tired, guarded, mildly irritated look", "zh": "疲惫、戒备而略显烦躁"},
    "Vincent": {"enabled": True, "en": "a bright, curious, slightly surprised look", "zh": "明亮、好奇而略带惊讶的神情"},
    "Willy": {"enabled": True, "en": "a cigarette-holding, tired stern look", "zh": "叼烟、疲惫而严肃的神情"},
    "Wizard": {"enabled": False, "en": "no usable frame 3 in the installed portrait", "zh": "当前肖像没有可用的第 3 帧", "reason": "installed Wizard sheet is only 128x64"},
    # SVE profiles.
    "Alesia": {"enabled": True, "en": "a confident smile with a hand near her face", "zh": "手靠近脸颊、自信的微笑"},
    "Andy": {"enabled": True, "en": "a friendly, engaged smile", "zh": "友善而投入的微笑"},
    "Apples": {"enabled": False, "en": "a plain neutral apple face", "zh": "普通中性的苹果表情", "reason": "no useful distinction from the neutral frame"},
    "Camilla": {"enabled": True, "en": "a playful, knowing smile", "zh": "俏皮而会意的微笑"},
    "Claire": {"enabled": True, "en": "an angry look with a red anger symbol", "zh": "带红色怒气符号的生气表情"},
    "Gunther": {"enabled": True, "en": "a composed side glance beneath a raised hat", "zh": "抬帽下从容的侧目"},
    "Hank": {"enabled": False, "en": "no usable frame 3", "zh": "没有可用的第 3 帧", "reason": "NoPortraits placeholder has no portrait sheet"},
    "Henchman": {"enabled": False, "en": "no usable frame 3", "zh": "没有可用的第 3 帧", "reason": "frame 3 is a solid magenta missing-texture tile"},
    "Isaac": {"enabled": True, "en": "a calm, confident half-smile", "zh": "平静而自信的浅笑"},
    "Jadu": {"enabled": True, "en": "a serious, skeptical side glance", "zh": "严肃而狐疑的侧目"},
    "Jolyne": {"enabled": True, "en": "an angry look with a red anger symbol", "zh": "带红色怒气符号的生气表情"},
    "Lance": {"enabled": True, "en": "a stern, displeased side glance", "zh": "严肃而不悦的侧目"},
    "Magnus": {"enabled": False, "en": "a third-party-dependent arcane expression", "zh": "取决于第三方肖像包的奥术表情", "reason": "OhoDavi and Rasmodia overlays give conflicting frame-3 semantics"},
    "Marlon": {"enabled": False, "en": "no usable frame 3", "zh": "没有可用的第 3 帧", "reason": "installed seasonal portrait is only one 64x64 tile"},
    "Martin": {"enabled": True, "en": "a worried, serious look", "zh": "担心而严肃的神情"},
    "Morgan": {"enabled": True, "en": "an eyes-closed, broad cheerful smile", "zh": "闭眼而开朗的灿烂微笑"},
    "Morris": {"enabled": True, "en": "a confident, exaggerated grin", "zh": "自信而夸张的咧嘴笑"},
    "Olivia": {"enabled": True, "en": "a composed, softly amused smile", "zh": "从容而带着轻微玩味的微笑"},
    "Peaches": {"enabled": False, "en": "no usable frame 3", "zh": "没有可用的第 3 帧", "reason": "portrait is only one 64x64 tile"},
    "Scarlett": {"enabled": True, "en": "an angry look with a red anger symbol", "zh": "带红色怒气符号的生气表情"},
    "Sophia": {"enabled": True, "en": "a frowning look with a red anger symbol", "zh": "带红色怒气符号的皱眉表情"},
    "Susan": {"enabled": True, "en": "a serious, displeased side glance", "zh": "严肃而不悦的侧目"},
    "Treyvon": {"enabled": False, "en": "no usable frame 3", "zh": "没有可用的第 3 帧", "reason": "NoPortraits placeholder has no portrait sheet"},
    "Victor": {"enabled": True, "en": "a polite, relaxed smile", "zh": "礼貌而放松的微笑"},
}

# A few packs reuse an NPC name while drawing a materially different frame 3.
# Keep these overrides next to the audit data instead of making the runtime guess
# from an asset filename that Content Patcher may have already replaced.
SOURCE_SEMANTIC_OVERRIDES: dict[tuple[str, str], dict[str, object]] = {
    ("oho", "Morris"): {
        "enabled": True,
        "en": "a composed, knowing half-smile",
        "zh": "从容而会意的浅笑",
    },
    ("seasonal", "Sandy"): {
        "enabled": True,
        "en": "an eyes-closed, gentle smile",
        "zh": "闭眼而温柔的微笑",
    },
    ("sve", "Sandy"): {
        "enabled": True,
        "en": "a playful side-profile kiss",
        "zh": "俏皮的侧身亲吻姿势",
    },
}

# Frame 3 can change meaning between outfit/portrait files within one pack.  The
# longest matching path rule wins, so a narrow placeholder exception can safely
# override a broader source-level description.
SOURCE_ASSET_SEMANTIC_OVERRIDES: dict[tuple[str, str, str], dict[str, object]] = {
    ("sve", "Magnus", "magnus/magnus.png"): {
        "enabled": True,
        "en": "a wide-eyed, alert surprise",
        "zh": "睁大眼、警觉而惊讶",
    },
    ("seasonal", "Magnus", "magnus/"): {
        "enabled": True,
        "en": "a wide-eyed, alert surprise",
        "zh": "睁大眼、警觉而惊讶",
    },
    ("addon", "Magnus", "magnus.png"): {
        "enabled": True,
        "en": "a guarded, skeptical sidelong glance with folded arms",
        "zh": "抱臂、戒备而怀疑地侧看",
    },
    ("rasmodia", "Magnus", "creepykat's/witch_sve.png"): {
        "enabled": True,
        "en": "a bright, open smile beneath the hat",
        "zh": "帽檐下坦然而明快地笑",
    },
    ("rasmodia", "Wizard", "creepykat's/witch_"): {
        "enabled": True,
        "en": "a soft, eyes-closed smile beneath the hat",
        "zh": "帽檐下闭眼柔和地笑",
    },
    ("rasmodia", "Wizard", "dacar's/"): {
        "enabled": True,
        "en": "a wide-eyed, uncertain upward side glance",
        "zh": "睁大眼、不确定地向上侧看",
    },
    ("rasmodia", "Wizard", "dacar's/witch_nonsve.png"): {
        "enabled": True,
        "en": "a cheerful, eyes-closed smile",
        "zh": "闭眼开心地笑",
    },
    ("rasmodia", "Wizard", "dacar's/hatless/witch_nonsve.png"): {
        "enabled": True,
        "en": "a cheerful, eyes-closed smile",
        "zh": "闭眼开心地笑",
    },
    ("rasmodia", "Magnus", "dacar's/witch_sve.png"): {
        "enabled": True,
        "en": "a wide-eyed, uncertain upward side glance",
        "zh": "睁大眼、不确定地向上侧看",
    },
    ("rasmodia", "Magnus", "dacar's/hatless/witch_sve.png"): {
        "enabled": True,
        "en": "a wide-eyed, uncertain upward side glance",
        "zh": "睁大眼、不确定地向上侧看",
    },
    ("rasmodia", "Magnus", "nyapu/witch_sve.png"): {
        "enabled": True,
        "en": "a warm, eyes-closed smile with hands clasped to her chest",
        "zh": "闭眼温暖地笑、双手合在胸前",
    },
    ("rasmodia", "Magnus", "original/witch_sve.png"): {
        "enabled": True,
        "en": "a wide-eyed, alert surprise",
        "zh": "睁大眼、警觉而惊讶",
    },
    ("rasmodia", "Magnus", "dacar's/witch_sve_romras_12.png"): {
        "enabled": True,
        "en": "a cheerful, eyes-closed smile",
        "zh": "闭眼开心地笑",
    },
    ("rasmodia", "Magnus", "dacar's/hatless/witch_sve_romras_12.png"): {
        "enabled": True,
        "en": "a cheerful, eyes-closed smile",
        "zh": "闭眼开心地笑",
    },
    ("rasmodia", "Magnus", "nyapu/hatless/witch_sve_romras_12.png"): {
        "enabled": True,
        "en": "a warm, eyes-closed smile with hands clasped to her chest",
        "zh": "闭眼温暖地笑、双手合在胸前",
    },
    ("rasmodia", "Magnus", "nyapu/hatless/witch_sve.png"): {
        "enabled": True,
        "en": "a warm, eyes-closed smile with hands clasped to her chest",
        "zh": "闭眼温暖地笑、双手合在胸前",
    },
    ("rasmodia", "Magnus", "nyapu/witch_sve_romras_12.png"): {
        "enabled": True,
        "en": "a warm, eyes-closed smile with hands clasped to her chest",
        "zh": "闭眼温暖地笑、双手合在胸前",
    },
    ("rasmodia", "Wizard", "nyapu/"): {
        "enabled": True,
        "en": "a warm, eyes-closed smile with hands clasped to her chest",
        "zh": "闭眼温暖地笑、双手合在胸前",
    },
    ("rasmodia", "Magnus", "original/witch_sve_romras_12.png"): {
        "enabled": True,
        "en": "a gentle, eyes-closed smile",
        "zh": "闭眼温柔地笑",
    },
    ("rasmodia", "Wizard", "original/"): {
        "enabled": True,
        "en": "a wide-eyed, alert surprise",
        "zh": "睁大眼、警觉而惊讶",
    },
    ("rasmodia", "Wizard", "original/witch_nonsve.png"): {
        "enabled": True,
        "en": "a gentle, eyes-closed smile",
        "zh": "闭眼温柔地笑",
    },
    ("rasmodia", "Magnus", "creepykat's/hatless/witch_sve.png"): {
        "enabled": False,
        "en": "a 404 All Hat No Play placeholder",
        "zh": "“404 All Hat No Play”占位图",
        "reason": "the portrait is a repeated placeholder with no expression",
    },
    ("rasmodia", "Magnus", "creepykat's/hatless/witch_sve_romras_12.png"): {
        "enabled": False,
        "en": "a 404 All Hat No Play placeholder",
        "zh": "“404 All Hat No Play”占位图",
        "reason": "the portrait is a repeated placeholder with no expression",
    },
    ("rasmodia", "Magnus", "original/hatless/witch_sve.png"): {
        "enabled": False,
        "en": "a 404 All Hat No Play placeholder",
        "zh": "“404 All Hat No Play”占位图",
        "reason": "the portrait is a repeated placeholder with no expression",
    },
    ("rasmodia", "Magnus", "original/hatless/witch_sve_romras_12.png"): {
        "enabled": False,
        "en": "a 404 All Hat No Play placeholder",
        "zh": "“404 All Hat No Play”占位图",
        "reason": "the portrait is a repeated placeholder with no expression",
    },
}

ALIASES = {
    "GuntherSilvian": "Gunther",
    "MorrisTod": "Morris",
    "MarlonFay": "Marlon",
    "Magnus": "Magnus",
    # The vanilla game stores Leo's portrait under his pre-island asset name.
    "ParrotBoy": "Leo",
}

SOURCE_NAMES: dict[str, set[str]] = {
    "oho": set(PROFILES),
    "vanilla": set(),
    "seasonal": {
        "Alesia", "Andy", "Camilla", "Claire", "Gunther", "Isaac", "Jadu", "Jolyne", "Lance",
        "Magnus", "Marlon", "Martin", "Morgan", "Morris", "Olivia", "Scarlett", "Sophia", "Susan",
        "Sandy", "Victor", "Wizard",
    },
    "sve": {
        "Alesia", "Andy", "Apples", "Camilla", "Claire", "Gunther", "Hank", "Henchman", "Isaac",
        "Jadu", "Jolyne", "Lance", "Magnus", "Marlon", "Martin", "Morgan", "Morris", "Olivia",
        "Peaches", "Sandy", "Scarlett", "Sophia", "Susan", "Treyvon", "Victor",
    },
    "addon": {"Gunther", "Magnus", "Olivia", "Sophia"},
    "rasmodia": {"Magnus", "Wizard"},
}

# The unmodded game uses different artwork from OhoDavi.  Keep these descriptions
# per source so adding the vanilla hashes cannot silently inherit a custom-pack
# meaning for the same NPC.
VANILLA_SEMANTICS: dict[str, dict[str, object]] = {
    "Abigail": {"enabled": True, "en": "a bright, content smile", "zh": "明亮而满足的微笑"},
    "Alex": {"enabled": True, "en": "holding a gridball with confident focus", "zh": "抱着格球、专注而自信"},
    "Caroline": {"enabled": True, "en": "a calm, neutral look", "zh": "平静而中性的神情"},
    "Clint": {"enabled": True, "en": "a knowing wink", "zh": "会意地眨眼"},
    "Demetrius": {"enabled": True, "en": "a calm, thoughtful look", "zh": "平静而若有所思的神情"},
    "Dwarf": {"enabled": False, "en": "no usable frame 3", "zh": "没有可用的第 3 帧"},
    "Elliott": {"enabled": True, "en": "a warm side-profile smile", "zh": "侧身而温和的微笑"},
    "Emily": {"enabled": True, "en": "a calm, soft smile", "zh": "平静而柔和的微笑"},
    "Evelyn": {"enabled": True, "en": "a tired, wistful, eyes-closed look", "zh": "疲惫、惆怅而闭眼的神情"},
    "George": {"enabled": True, "en": "an angry, clenched grimace", "zh": "生气而咬牙的表情"},
    "Gus": {"enabled": True, "en": "a friendly, content smile", "zh": "友善而满足的微笑"},
    "Haley": {"enabled": True, "en": "a bright, calm smile", "zh": "明亮而平静的微笑"},
    "Harvey": {"enabled": True, "en": "a relaxed smile behind his glasses", "zh": "戴着眼镜放松地微笑"},
    "Jas": {"enabled": True, "en": "a curious, slightly uncertain look", "zh": "好奇而略显不确定的神情"},
    "Jodi": {"enabled": True, "en": "a gentle, composed smile", "zh": "温柔而从容的微笑"},
    "Kent": {"enabled": True, "en": "an angry, teeth-clenched grimace", "zh": "生气而咬紧牙关的表情"},
    "Krobus": {"enabled": True, "en": "wide-eyed surprise", "zh": "睁大眼睛的惊讶"},
    "Leah": {"enabled": True, "en": "a warm, open smile", "zh": "温暖而坦然的微笑"},
    "Leo": {"enabled": True, "en": "a gentle, slightly shy smile", "zh": "温和而略显害羞的微笑"},
    "Lewis": {"enabled": True, "en": "a calm, stern look", "zh": "平静而略严肃的神情"},
    "Linus": {"enabled": True, "en": "a warm, open smile", "zh": "温暖而坦然的微笑"},
    "Marnie": {"enabled": True, "en": "a soft, friendly smile", "zh": "柔和而友善的微笑"},
    "Maru": {"enabled": True, "en": "a hand-to-glasses, eyes-closed smile", "zh": "扶着眼镜、闭眼微笑"},
    "Pam": {"enabled": True, "en": "an open-mouthed angry outburst", "zh": "张嘴怒喊的表情"},
    "Penny": {"enabled": True, "en": "a gentle side-profile smile", "zh": "侧身而温柔的微笑"},
    "Pierre": {"enabled": True, "en": "a guarded, knowing half-smile", "zh": "戒备而会意的浅笑"},
    "Robin": {"enabled": True, "en": "an eyes-closed, relaxed smile", "zh": "闭眼而放松的微笑"},
    "Sam": {"enabled": True, "en": "an eyes-closed, happy smile", "zh": "闭眼而开心的微笑"},
    "Sandy": {"enabled": True, "en": "a playful side-profile kiss", "zh": "俏皮的侧身亲吻姿势"},
    "Sebastian": {"enabled": True, "en": "a calm, faint smile", "zh": "平静而淡淡的微笑"},
    "Shane": {"enabled": True, "en": "a small, guarded smile", "zh": "克制而浅浅的微笑"},
    "Vincent": {"enabled": True, "en": "a bright, wide-eyed look", "zh": "明亮而睁大眼睛的神情"},
    "Willy": {"enabled": True, "en": "a tired, cigarette-holding look", "zh": "叼烟而疲惫的神情"},
    "Wizard": {"enabled": False, "en": "no usable frame 3", "zh": "没有可用的第 3 帧"},
}
SOURCE_NAMES["vanilla"] = set(VANILLA_SEMANTICS)

# These variants visibly change the pose rather than only the clothing.  They
# stay in the audit, but their hashes aren't enabled by the generated catalog.
EXCLUDED_VARIANT_MARKERS = {("Maru", "oho"): ("Beach", "Hospital")}

EXTRA_FRAME_SEMANTICS = {
    **VANILLA_EXTRA_SEMANTICS,
    **SVE_EXTRA_SEMANTICS,
}
EXTRA_FRAME_ASSET_OVERRIDES = {
    **VANILLA_ASSET_OVERRIDES,
    **SVE_ASSET_OVERRIDES,
}

# These files are partial overlays or explicit placeholders, not usable portrait
# sheets. They remain visible in the optional audit inventory but never become
# runtime candidates on their own.
NON_RUNTIME_ASSET_RULES: dict[tuple[str, str], dict[str, str]] = {
    **{
        ("seasonal", suffix): {
            "en": "a partial Content Patcher overlay, not a complete portrait frame",
            "zh": "Content Patcher 的局部覆盖层，并非完整肖像帧",
            "reason": "partial overlay assets are not standalone runtime portraits",
        }
        for suffix in (
            "sophia/sophia_older_overlay.png",
            "sophia/sophia_older_mu_overlay.png",
        )
    },
    **{
        ("rasmodia", suffix): {
            "en": "a repeated 404 All Hat No Play placeholder",
            "zh": "重复的“404 All Hat No Play”占位图",
            "reason": "placeholder portrait sheet with no usable expression semantics",
        }
        for suffix in (
            "creepykat's/hatless/witch_beach.png",
            "creepykat's/hatless/witch_flowerdance.png",
            "creepykat's/hatless/witch_nonsve.png",
            "creepykat's/hatless/witch_sve.png",
            "creepykat's/hatless/witch_sve_romras_12.png",
            "original/hatless/witch_beach.png",
            "original/hatless/witch_flowerdance.png",
            "original/hatless/witch_nonsve.png",
            "original/hatless/witch_sve.png",
            "original/hatless/witch_sve_romras_12.png",
        )
    },
}

SOPHIA_OLDER_COMPOSITES = {
    "Sophia_older_overlay.png": (
        "Sophia_Spring.png",
        "Sophia_Summer.png",
        "Sophia_Fall.png",
        "Sophia_Winter_Outdoor.png",
        "Sophia_Winter_Indoor.png",
        "Sophia_FlowerDance.png",
        "Sophia_Beach.png",
    ),
    "Sophia_older_mu_overlay.png": (
        "Sophia_Spring_Makeup.png",
        "Sophia_Summer_Makeup.png",
        "Sophia_Fall_Makeup.png",
        "Sophia_Winter_Outdoor_Makeup.png",
        "Sophia_Winter_Indoor_Makeup.png",
        "Sophia_SpiritsEve.png",
    ),
}


def semantic_for(name: str, kind: str, asset: str = "") -> dict[str, object]:
    normalized_asset = asset.replace("\\", "/").lower()
    matching = [
        (len(path_fragment), semantic)
        for (rule_kind, rule_name, path_fragment), semantic in SOURCE_ASSET_SEMANTIC_OVERRIDES.items()
        if rule_kind == kind
        and rule_name == name
        and path_fragment.lower() in normalized_asset
    ]
    if matching:
        return dict(max(matching, key=lambda item: item[0])[1])
    if (kind, name) in SOURCE_SEMANTIC_OVERRIDES:
        return SOURCE_SEMANTIC_OVERRIDES[(kind, name)]
    return VANILLA_SEMANTICS[name] if kind == "vanilla" else PROFILES[name]


def canonical_rgba_bytes(tile: Image.Image) -> bytes:
    """Match the premultiplied colors returned by Texture2D.GetData at runtime.

    SMAPI loads mod PNGs through ``SKPMColor.PreMultiply`` before uploading them to a
    ``Texture2D``. The vanilla XNB pipeline uses the same premultiplied-alpha convention.
    Hashing the source PNG bytes directly would therefore miss any portrait containing
    partially transparent pixels even though the visible image is one we reviewed.
    """
    rgba = bytearray(tile.tobytes())
    for offset in range(0, len(rgba), 4):
        alpha = rgba[offset + 3]
        for channel in range(3):
            rgba[offset + channel] = (rgba[offset + channel] * alpha + 127) // 255
    return bytes(rgba)


def tile_status(tile: Image.Image) -> str:
    pixels = list(tile.get_flattened_data())
    if not any(pixel[3] for pixel in pixels):
        return "transparent"
    if len(set(pixels)) == 1:
        return "solid"
    return "usable"


def read_tile(image: Image.Image, index: int) -> tuple[Image.Image | None, str]:
    """Read one row-major 64x64 tile and classify missing/placeholder data."""
    columns = image.width // TILE_SIZE
    if columns < 1:
        return None, "out_of_bounds"

    x = (index % columns) * TILE_SIZE
    y = (index // columns) * TILE_SIZE
    if x + TILE_SIZE > image.width or y + TILE_SIZE > image.height:
        return None, "out_of_bounds"

    tile = image.crop((x, y, x + TILE_SIZE, y + TILE_SIZE))
    return tile, tile_status(tile)


def inventory_name(path: Path, root: Path, root_kind: str) -> str:
    """Best-effort NPC label for audit records, including unprofiled assets."""
    stem = path.stem
    if root_kind == "rasmodia":
        if stem.startswith("Witch_SVE"):
            candidate = "Magnus"
        elif stem.startswith("Witch") or stem.startswith("Rasmodia"):
            candidate = "Wizard"
        else:
            candidate = stem.split("_")[0]
    elif root_kind == "addon":
        candidate = stem.split("_")[0]
    elif root_kind == "vanilla" or path.parent == root or path.parent.name == "NoPortraits":
        candidate = stem.split("_")[0]
    else:
        candidate = path.parent.name

    return ALIASES.get(candidate, candidate)


def reviewed_extra_semantic(
    name: str,
    kind: str,
    index: int,
    asset: str,
) -> dict[str, str] | None:
    """Resolve the narrowest reviewed numeric-frame rule for one source asset."""
    normalized_asset = asset.replace("\\", "/").lower()
    matching_overrides = [
        (len(asset_substring), semantic)
        for (rule_kind, rule_name, rule_index, asset_substring), semantic
        in EXTRA_FRAME_ASSET_OVERRIDES.items()
        if rule_kind == kind
        and rule_name == name
        and rule_index == index
        and asset_substring.lower() in normalized_asset
    ]
    if matching_overrides:
        return dict(max(matching_overrides, key=lambda item: item[0])[1])

    semantic = EXTRA_FRAME_SEMANTICS.get((kind, name, index))
    return dict(semantic) if semantic is not None else None


def inventory_semantic(
    name: str,
    kind: str,
    index: int,
    status: str,
    reviewed_source: bool,
    asset: str,
) -> dict[str, object]:
    """Return conservative, localized metadata for one audited frame."""
    if status != "usable":
        status_text = {
            "transparent": ("a fully transparent frame", "完全透明的帧"),
            "solid": ("a solid placeholder frame", "纯色占位帧"),
            "out_of_bounds": ("no frame exists at this index", "该索引超出肖像范围"),
        }.get(status, ("an unreadable frame", "无法读取的帧"))
        return {
            "enabled": False,
            "decision": "deny",
            "review": "unusable",
            "en": status_text[0],
            "zh": status_text[1],
            "reason": f"frame status is {status}",
        }

    if index == 3:
        if not reviewed_source or name not in PROFILES:
            return {
                "enabled": False,
                "decision": "unknown",
                "review": "unreviewed",
                "en": "an asset-specific unique expression",
                "zh": "未审核的资源特有表情",
                "reason": "NPC or portrait source is outside the reviewed semantic profiles",
            }

        semantic = semantic_for(name, kind, asset)
        result = {
            "enabled": bool(semantic["enabled"]),
            "decision": "allow" if semantic["enabled"] else "deny",
            "review": "manual-per-source",
            "en": semantic["en"],
            "zh": semantic["zh"],
        }
        if not result["enabled"]:
            result["reason"] = semantic.get("reason", "reviewed frame is intentionally disabled")
        return result

    spec = STANDARD_FRAME_SPECS.get(index)
    if spec is None:
        semantic = reviewed_extra_semantic(name, kind, index, asset)
        if semantic is not None:
            decision = semantic["decision"]
            result: dict[str, object] = {
                "enabled": decision == "allow",
                "decision": decision,
                "review": "manual-numeric-frame",
                "en": semantic["en"],
                "zh": semantic["zh"],
            }
            if decision != "allow":
                result["reason"] = semantic["reason"]
            return result

        return {
            "enabled": False,
            "decision": "unknown",
            "review": "numeric-unreviewed",
            "en": f"an unreviewed asset-specific numeric frame ({index})",
            "zh": f"未审核的资源特有数字帧（{index}）",
            "reason": "numeric frames above the standard six are not automatically safe for AI output",
        }

    result = {
        "enabled": bool(spec["enabled"] and reviewed_source),
        "decision": "allow" if spec["enabled"] and reviewed_source else "unknown",
        "review": spec["review"],
        "en": spec["en"],
        "zh": spec["zh"],
    }
    if not result["enabled"]:
        result["reason"] = (
            "NPC or portrait source is outside the reviewed semantic profiles"
            if not reviewed_source
            else spec.get("reason", "standard frame is not enabled")
        )
    return result


def build_inventory_image(
    image: Image.Image,
    name: str,
    kind: str,
    label: str,
    rel: str,
    *,
    runtime_candidate: bool = True,
    runtime_exclusion: dict[str, str] | None = None,
) -> dict[str, object]:
    """Build one decoded audit record, including conservative runtime metadata."""
    reviewed_source = name in PROFILES and name in SOURCE_NAMES.get(kind, set())
    excluded_markers = EXCLUDED_VARIANT_MARKERS.get((name, kind), ())
    excluded = any(marker.lower() in rel.lower() for marker in excluded_markers)

    image = image.convert("RGBA")
    columns = image.width // TILE_SIZE
    rows = image.height // TILE_SIZE
    tile_count = columns * rows
    frame_count = max(STANDARD_FRAME_COUNT, tile_count)
    frames: list[dict[str, object]] = []
    for index in range(frame_count):
        tile, status = read_tile(image, index)
        marker = STANDARD_FRAME_SPECS.get(index, {}).get("marker", str(index))
        semantic = inventory_semantic(name, kind, index, status, reviewed_source, rel)
        if not runtime_candidate and status == "usable":
            semantic = {
                "enabled": False,
                "decision": "deny",
                "review": "non-runtime-asset",
                **(runtime_exclusion or {
                    "en": "a non-runtime portrait asset",
                    "zh": "非运行时肖像资源",
                    "reason": "asset is not a standalone runtime portrait",
                }),
            }
        elif excluded and index == 3 and status == "usable":
            semantic = {
                "enabled": False,
                "decision": "deny",
                "review": "manual-excluded-variant",
                "en": "an excluded variant-specific pose",
                "zh": "已排除的变体特有姿势",
                "reason": "this variant changes the reviewed frame-3 pose",
            }

        frame: dict[str, object] = {
            "index": index,
            "marker": marker,
            "status": status,
            "hash": None,
            **semantic,
        }
        if tile is not None:
            frame["hash"] = hashlib.sha256(canonical_rgba_bytes(tile)).hexdigest().upper()
        frames.append(frame)

    deny_conflicting_same_asset_duplicates(frames)

    return {
        "source": label,
        "kind": kind,
        "npc": name,
        "asset": rel,
        "runtimeCandidate": runtime_candidate,
        "width": image.width,
        "height": image.height,
        "columns": columns,
        "rows": rows,
        "partialEdge": (image.width % TILE_SIZE != 0 or image.height % TILE_SIZE != 0),
        "frames": frames,
    }


def deny_conflicting_same_asset_duplicates(frames: list[dict[str, object]]) -> None:
    """Disable non-neutral markers whose pixels have incompatible meanings in one sheet.

    Slot conventions are only a starting point. If one actual portrait image repeats identical
    pixels in two slots, those pixels cannot truthfully be both e.g. sad and angry. Neutral index 0
    remains safe; every conflicting non-neutral allow entry fails closed instead of teaching the
    model a visual distinction the installed asset does not contain.
    """
    by_hash: dict[str, list[dict[str, object]]] = {}
    for frame in frames:
        hashed = frame.get("hash")
        if frame.get("status") == "usable" and isinstance(hashed, str) and hashed:
            by_hash.setdefault(hashed, []).append(frame)

    for duplicates in by_hash.values():
        allowed = [frame for frame in duplicates if frame.get("decision") == "allow"]
        meanings = {
            (str(frame.get("en", "")).strip(), str(frame.get("zh", "")).strip())
            for frame in allowed
        }
        if len(meanings) <= 1:
            continue

        conflicting_indexes = sorted(int(frame["index"]) for frame in allowed)
        for frame in allowed:
            if int(frame["index"]) == 0:
                continue

            frame["enabled"] = False
            frame["decision"] = "deny"
            frame["review"] = "same-asset-duplicate-conflict"
            frame["reason"] = (
                "identical pixels have conflicting reviewed meanings at frame indexes "
                + ", ".join(str(index) for index in conflicting_indexes)
            )


def build_inventory_asset(
    path: Path,
    root: Path,
    kind: str,
    label: str,
) -> dict[str, object]:
    """Build one asset record, continuing through bad files."""
    name = inventory_name(path, root, kind)
    relative_path = path.relative_to(root).as_posix()
    rel = f"{label}/{relative_path}"
    normalized_relative_path = relative_path.replace("\\", "/").lower()
    matching_exclusions = [
        (len(suffix), rule)
        for (rule_kind, suffix), rule in NON_RUNTIME_ASSET_RULES.items()
        if rule_kind == kind and normalized_relative_path.endswith(suffix.lower())
    ]
    runtime_exclusion = max(matching_exclusions, key=lambda item: item[0])[1] if matching_exclusions else None
    runtime_candidate = runtime_exclusion is None

    try:
        with Image.open(path) as source_image:
            return build_inventory_image(
                source_image,
                name,
                kind,
                label,
                rel,
                runtime_candidate=runtime_candidate,
                runtime_exclusion=runtime_exclusion,
            )
    except Exception as exc:
        frames = []
        for index in range(STANDARD_FRAME_COUNT):
            marker = STANDARD_FRAME_SPECS.get(index, {}).get("marker", str(index))
            frames.append({
                "index": index,
                "marker": marker,
                "status": "decode_error",
                "hash": None,
                "enabled": False,
                "decision": "deny",
                "review": "unusable",
                "en": "an unreadable frame",
                "zh": "无法读取的帧",
                "reason": f"could not decode asset: {exc}",
            })
        return {
            "source": label,
            "kind": kind,
            "npc": name,
            "asset": rel,
            "runtimeCandidate": False,
            "width": 0,
            "height": 0,
            "columns": 0,
            "rows": 0,
            "partialEdge": False,
            "frames": frames,
        }


def add_inventory_files(
    inventory: list[dict[str, object]],
    root: Path,
    kind: str,
    label: str,
) -> None:
    if not root or not root.exists():
        return
    for path in sorted(
        candidate
        for candidate in root.rglob("*")
        if candidate.is_file() and candidate.suffix.lower() == ".png"
    ):
        inventory.append(build_inventory_asset(path, root, kind, label))


def source_fingerprint(root: Path) -> str:
    """Return the content/version fingerprint for an audited PNG source tree.

    The relative path and each file's SHA-256 are included so a source pack which
    happens to retain the same PNG count cannot silently reuse stale semantic labels.
    Paths are NFC-normalized before sorting/encoding to keep the manifest stable on
    Windows and Linux without changing the asset names retained in the audit output.
    """
    files = [
        candidate
        for candidate in root.rglob("*")
        if candidate.is_file() and candidate.suffix.lower() == ".png"
    ]
    files.sort(
        key=lambda candidate: unicodedata.normalize(
            "NFC", candidate.relative_to(root).as_posix()
        )
    )

    digest = hashlib.sha256()
    digest.update(b"LivingNPCsPortraitSourceV1\0")
    digest.update(struct.pack(">I", len(files)))
    for path in files:
        relative = unicodedata.normalize("NFC", path.relative_to(root).as_posix()).encode("utf-8")
        data = path.read_bytes()
        digest.update(struct.pack(">I", len(relative)))
        digest.update(relative)
        digest.update(struct.pack(">Q", len(data)))
        digest.update(hashlib.sha256(data).digest())

    return digest.hexdigest().upper()


def validate_source_paths(
    sources: dict[str, Path],
    *,
    allow_source_drift: bool,
) -> list[str]:
    """Return actionable validation errors for the audited raw portrait inputs."""
    errors: list[str] = []
    for source, path in sources.items():
        option = f"--{source}"
        if not path.exists():
            errors.append(f"{option} directory does not exist: {path}")
            continue
        if not path.is_dir():
            errors.append(f"{option} must be a directory, but is not: {path}")
            continue
        if allow_source_drift:
            continue

        try:
            actual_count = sum(
                1
                for candidate in path.rglob("*")
                if candidate.is_file() and candidate.suffix.lower() == ".png"
            )
        except OSError as exc:
            errors.append(f"could not scan {option} directory {path}: {exc}")
            continue

        expected_count = EXPECTED_SOURCE_PNG_COUNTS[source]
        if actual_count != expected_count:
            errors.append(
                f"{option} expected {expected_count} raw PNG files, found {actual_count} in {path}; "
                "verify the audited source version/layout or pass --allow-source-drift to "
                "acknowledge the source change"
            )

        try:
            actual_fingerprint = source_fingerprint(path)
        except OSError as exc:
            errors.append(f"could not fingerprint {option} directory {path}: {exc}")
            continue

        expected_fingerprint = EXPECTED_SOURCE_FINGERPRINTS[source]
        if actual_fingerprint != expected_fingerprint:
            errors.append(
                f"{option} source fingerprint changed (expected {expected_fingerprint}, "
                f"found {actual_fingerprint} in {path}); verify the audited source version/layout "
                "or pass --allow-source-drift to acknowledge the source change"
            )

    return errors


def add_sophia_older_composites(
    inventory: list[dict[str, object]],
    root: Path,
    label: str,
) -> None:
    """Reproduce Seasonal Cute SVE's base+older-overlay final portrait pixels."""
    sophia_root = root / "Sophia"
    for overlay_name, base_names in SOPHIA_OLDER_COMPOSITES.items():
        overlay_path = sophia_root / overlay_name
        if not overlay_path.exists():
            continue

        with Image.open(overlay_path) as source_overlay:
            overlay = source_overlay.convert("RGBA")
            for base_name in base_names:
                base_path = sophia_root / base_name
                if not base_path.exists():
                    continue

                with Image.open(base_path) as source_base:
                    base = source_base.convert("RGBA")
                    if overlay.width < base.width or overlay.height < base.height:
                        continue

                    layer = overlay.crop((0, 0, base.width, base.height))
                    composite = Image.alpha_composite(base, layer)
                    rel = (
                        f"{label}/Sophia/{base_path.stem}"
                        f"+{overlay_path.stem}.png"
                    )
                    inventory.append(build_inventory_image(
                        composite,
                        "Sophia",
                        "seasonal",
                        label,
                        rel,
                    ))


def marker_for_index(index: int) -> str:
    return str(STANDARD_FRAME_SPECS.get(index, {}).get("marker", index))


def build_runtime_entries(inventory: list[dict[str, object]]) -> list[dict[str, object]]:
    """Collapse the audit inventory into fail-closed runtime entries."""
    grouped: dict[tuple[int, str], list[dict[str, object]]] = {}
    for asset in inventory:
        if not asset.get("runtimeCandidate", True):
            continue
        for frame in asset["frames"]:
            if frame["status"] != "usable" or not frame.get("hash"):
                continue

            index = int(frame["index"])
            hashed = str(frame["hash"])
            grouped.setdefault((index, hashed), []).append({
                **frame,
                "asset": asset["asset"],
                "npc": asset["npc"],
                "source": asset["source"],
            })

    entries: list[dict[str, object]] = []
    for (index, hashed), candidates in sorted(grouped.items()):
        marker = marker_for_index(index)
        explicit_denials = [candidate for candidate in candidates if candidate["decision"] == "deny"]
        allowed = [candidate for candidate in candidates if candidate["decision"] == "allow"]
        allowed_semantics = sorted({
            (str(candidate["en"]), str(candidate["zh"]))
            for candidate in allowed
        })

        enabled = False
        decision = "unknown"
        reason = "no reviewed semantic is available for this frame"
        if explicit_denials:
            decision = "deny"
            reasons = sorted({
                str(candidate.get("reason", "reviewed source explicitly disables this exact frame"))
                for candidate in explicit_denials
            })
            reason = "reviewed source denial: " + " | ".join(reasons)
        elif len(allowed_semantics) > 1:
            decision = "deny"
            reason = "the same frame index and hash has conflicting reviewed semantics: " + " | ".join(
                f"{english} / {chinese}" for english, chinese in allowed_semantics
            )
        elif len(allowed_semantics) == 1:
            enabled = True
            decision = "allow"
            reason = ""

        if enabled:
            english, chinese = allowed_semantics[0]
        elif explicit_denials:
            english = str(explicit_denials[0]["en"])
            chinese = str(explicit_denials[0]["zh"])
        elif allowed_semantics:
            english, chinese = allowed_semantics[0]
            if decision == "deny" and not reason:
                reason = "the same frame index and hash has conflicting reviewed semantics"
        else:
            english = str(candidates[0]["en"])
            chinese = str(candidates[0]["zh"])

        entry: dict[str, object] = {
            "index": index,
            "marker": marker,
            "hash": hashed,
            "enabled": enabled,
            "decision": decision,
            "en": english,
            "zh": chinese,
            "profiles": sorted({str(candidate["npc"]) for candidate in candidates}),
            "assets": sorted({str(candidate["asset"]) for candidate in candidates}),
        }
        if reason:
            entry["reason"] = reason
        entries.append(entry)

    return entries


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--vanilla", type=Path, required=True)
    parser.add_argument("--oho", type=Path, required=True)
    parser.add_argument("--sve-seasonal", type=Path, required=True)
    parser.add_argument("--sve-default", type=Path, required=True)
    parser.add_argument("--sve-addon", type=Path, required=True)
    parser.add_argument("--rasmodia", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--inventory-output", type=Path)
    parser.add_argument(
        "--allow-source-drift",
        action="store_true",
        help=(
            "allow audited source directories to contain different files or raw PNG counts; "
            "directories must still exist"
        ),
    )
    args = parser.parse_args()

    source_paths = {
        "vanilla": args.vanilla,
        "oho": args.oho,
        "sve-seasonal": args.sve_seasonal,
        "sve-default": args.sve_default,
        "sve-addon": args.sve_addon,
    }
    if args.rasmodia is not None:
        source_paths["rasmodia"] = args.rasmodia

    source_errors = validate_source_paths(
        source_paths,
        allow_source_drift=args.allow_source_drift,
    )
    if source_errors:
        parser.error("portrait source validation failed:\n  - " + "\n  - ".join(source_errors))

    inventory: list[dict[str, object]] = []
    add_inventory_files(inventory, args.vanilla, "vanilla", "Vanilla")
    add_inventory_files(inventory, args.oho, "oho", "OhoDavi")
    add_inventory_files(inventory, args.sve_seasonal, "seasonal", "SeasonalCuteSVE")
    add_sophia_older_composites(inventory, args.sve_seasonal, "SeasonalCuteSVE")
    add_inventory_files(inventory, args.sve_default, "sve", "SVE")
    add_inventory_files(inventory, args.sve_addon, "addon", "OhoDaviSVEAddon")
    if args.rasmodia:
        add_inventory_files(inventory, args.rasmodia, "rasmodia", "RomanceableRasmodia")

    inventory.sort(key=lambda asset: (str(asset["source"]), str(asset["asset"])))
    frame_entries = build_runtime_entries(inventory)
    inventory_status = Counter(
        frame["status"]
        for asset in inventory
        for frame in asset["frames"]
    )
    inventory_markers = Counter(
        frame["marker"]
        for asset in inventory
        for frame in asset["frames"]
        if frame["status"] == "usable"
    )
    inventory_by_source: dict[str, dict[str, int]] = {}
    for asset in inventory:
        source = str(asset["source"])
        source_stats = inventory_by_source.setdefault(source, {
            "assets": 0,
            "frames": 0,
            "usable": 0,
            "transparent": 0,
            "solid": 0,
            "out_of_bounds": 0,
            "decode_error": 0,
            "enabled": 0,
        })
        source_stats["assets"] += 1
        for frame in asset["frames"]:
            source_stats["frames"] += 1
            status = str(frame["status"])
            source_stats[status] = source_stats.get(status, 0) + 1
            if frame["enabled"]:
                source_stats["enabled"] += 1

    frame_inventory = {
        "Version": 1,
        "Description": "All 64x64 portrait tiles from the audited sources. Disabled records are retained for audit but are not automatically taught to the AI.",
        "TileSize": TILE_SIZE,
        "StandardIndices": {
            str(index): {
                "marker": spec["marker"],
                "en": spec["en"],
                "zh": spec["zh"],
            }
            for index, spec in STANDARD_FRAME_SPECS.items()
        },
        "Summary": {
            "assets": len(inventory),
            "frames": sum(len(asset["frames"]) for asset in inventory),
            "status": dict(sorted(inventory_status.items())),
            "usableByMarker": dict(sorted(inventory_markers.items())),
            "bySource": dict(sorted(inventory_by_source.items())),
        },
        "Assets": inventory,
    }

    if args.inventory_output:
        args.inventory_output.parent.mkdir(parents=True, exist_ok=True)
        args.inventory_output.write_text(
            json.dumps(frame_inventory, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
        )

    result = {
        "Version": 2,
        "TileSize": TILE_SIZE,
        "Description": "Reviewed full-frame semantics keyed by frame index and canonical pixel hash. Unknown or conflicting frames are intentionally disabled.",
        "AuditSources": [
            "Stardew Valley 1.6.15 vanilla portrait assets",
            "OhoDavi's StardewValley Anime Mods 1.6.4",
            "Seasonal Cute Characters SVE 3.0.0",
            "Stardew Valley Expanded portrait assets",
            "OhoDavi SVE Portrait Addons 1.0.0",
            "Romanceable Rasmodia portrait assets (where present)",
        ],
        "FrameEntries": frame_entries,
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    enabled = sum(1 for frame in frame_entries if frame["enabled"])
    print(f"wrote {args.output} ({enabled}/{len(frame_entries)} enabled index/hash entries)")
    if args.inventory_output:
        print(f"wrote {args.inventory_output} ({frame_inventory['Summary']['frames']} audited frame records)")


if __name__ == "__main__":
    main()
