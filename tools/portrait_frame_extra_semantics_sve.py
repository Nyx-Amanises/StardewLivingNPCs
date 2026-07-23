"""Reviewed semantics for SVE portrait frames above the standard six.

The broad table describes a numeric slot when its meaning is stable across the
installed SVE and Seasonal Cute SVE assets.  Asset overrides handle blank
tiles, embedded costumes, action props, exact duplicates, and partial Content
Patcher overlays without weakening the default fail-closed policy.
"""

from __future__ import annotations


def _allow(en: str, zh: str) -> dict[str, str]:
    return {"decision": "allow", "en": en, "zh": zh}


def _deny(en: str, zh: str, reason: str) -> dict[str, str]:
    return {"decision": "deny", "en": en, "zh": zh, "reason": reason}


_BLANK = _deny(
    "a fully transparent placeholder frame",
    "完全透明的占位帧",
    "fully transparent placeholder with no drawable expression",
)

_PARTIAL_OVERLAY = _deny(
    "a partial Content Patcher face overlay, not a complete portrait",
    "Content Patcher 的局部脸部覆盖层，并非完整肖像",
    "partial overlay assets are not standalone runtime portraits",
)


# Meanings shared by the original SVE portraits and Seasonal Cute SVE.
_COMMON_EXTRA_FRAME_SEMANTICS: dict[tuple[str, int], dict[str, str]] = {
    ("Alesia", 6): _BLANK,
    ("Alesia", 7): _BLANK,
    ("Andy", 6): _deny(
        "eyes closed while drinking from a jar",
        "闭着眼从罐子里喝饮料",
        "drink prop and drinking action; not a general-purpose expression",
    ),
    ("Andy", 7): _allow(
        "a furious, confrontational glare",
        "暴怒而充满敌意地瞪视",
    ),
    ("Andy", 8): _allow(
        "a dejected, ashamed look with lowered eyes or a lowered cap",
        "沮丧惭愧地垂眼或压低帽檐",
    ),
    ("Andy", 9): _BLANK,
    ("Camilla", 6): _allow(
        "a joyful, eyes-closed laugh",
        "闭眼开心大笑",
    ),
    ("Camilla", 7): _allow(
        "a surprised, uncertain look",
        "惊讶而迟疑",
    ),
    ("Camilla", 8): _allow(
        "a blushing, delighted, eyes-closed laugh",
        "脸红并闭眼欣喜大笑",
    ),
    ("Camilla", 9): _BLANK,
    ("Claire", 6): _allow(
        "a bright, eyes-closed laugh",
        "闭眼爽朗大笑",
    ),
    ("Claire", 7): _allow(
        "a gentle, contented, eyes-closed smile",
        "闭眼温柔满足地微笑",
    ),
    ("Claire", 8): _deny(
        "a neutral look in a Joja work uniform",
        "穿 Joja 工作服时的平静神情",
        "alternate work costume embedded in the general portrait sheet",
    ),
    ("Claire", 9): _deny(
        "a soft smile in a Joja work uniform",
        "穿 Joja 工作服时浅笑",
        "alternate work costume embedded in the general portrait sheet",
    ),
    ("Claire", 10): _deny(
        "an uneasy, annoyed look in a Joja work uniform",
        "穿 Joja 工作服时不安烦恼",
        "alternate work costume embedded in the general portrait sheet",
    ),
    ("Claire", 11): _deny(
        "a contented, eyes-closed smile in a Joja work uniform",
        "穿 Joja 工作服时闭眼满足地笑",
        "alternate work costume embedded in the general portrait sheet",
    ),
    ("Claire", 12): _deny(
        "a neutral look in a movie-theater uniform",
        "穿电影院制服时的平静神情",
        "alternate theater costume embedded in the general portrait sheet",
    ),
    ("Claire", 13): _deny(
        "a soft smile in a movie-theater uniform",
        "穿电影院制服时浅笑",
        "alternate theater costume embedded in the general portrait sheet",
    ),
    ("Claire", 14): _deny(
        "a worried look in a movie-theater uniform",
        "穿电影院制服时担忧",
        "alternate theater costume embedded in the general portrait sheet",
    ),
    ("Claire", 15): _deny(
        "a contented, eyes-closed smile in a movie-theater uniform",
        "穿电影院制服时闭眼满足地笑",
        "alternate theater costume embedded in the general portrait sheet",
    ),
    ("Isaac", 6): _allow(
        "a stern, serious look",
        "严肃冷峻",
    ),
    ("Isaac", 7): _allow(
        "a furious, teeth-bared outburst",
        "暴怒地咬牙呵斥",
    ),
    ("Isaac", 8): _allow(
        "an irritated, closed-mouth glare",
        "恼怒地闭嘴瞪视",
    ),
    ("Isaac", 9): _allow(
        "a worried, uneasy, teeth-clenched look",
        "忧虑不安并紧咬牙关",
    ),
    ("Jadu", 6): _allow(
        "a wide-eyed, open-mouthed shock",
        "睁大眼、张嘴震惊",
    ),
    ("Jadu", 7): _BLANK,
    ("Jadu", 8): _BLANK,
    ("Jadu", 9): _BLANK,
    ("Lance", 6): _allow(
        "a serious, contemplative look",
        "严肃沉思",
    ),
    ("Lance", 7): _deny(
        "a shirtless, calm look",
        "赤裸上身时的平静神情",
        "shirtless intimate-scene costume embedded in the general portrait sheet",
    ),
    ("Lance", 8): _deny(
        "a shirtless, happy smile",
        "赤裸上身时开心地笑",
        "shirtless intimate-scene costume embedded in the general portrait sheet",
    ),
    ("Lance", 9): _deny(
        "a shirtless, contented, eyes-closed smile",
        "赤裸上身时闭眼满足地笑",
        "shirtless intimate-scene costume embedded in the general portrait sheet",
    ),
    ("Magnus", 6): _deny(
        "a neutral pink-haired palette variant",
        "粉色头发与胡须的中性调色变体",
        "appearance and palette variant with no distinct emotion",
    ),
    ("Magnus", 7): _BLANK,
    ("Martin", 6): _allow(
        "a gentle, earnest slight smile with a sidelong glance",
        "真诚温和地浅笑并侧看",
    ),
    ("Martin", 7): _allow(
        "a quiet, friendly slight smile with the opposite sidelong glance",
        "安静友善地浅笑并看向另一侧",
    ),
    ("Morgan", 6): _allow(
        "a tired or calm, eyes-closed look",
        "疲倦或平静地闭眼",
    ),
    ("Morgan", 7): _allow(
        "a playful, conspiratorial wink",
        "调皮而心照不宣地眨眼",
    ),
    ("Morgan", 8): _allow(
        "a wary, uneasy side glance",
        "警惕不安地侧看",
    ),
    ("Morgan", 9): _allow(
        "a lonely, reflective side glance",
        "孤单而若有所思地侧看",
    ),
    ("Morris", 6): _allow(
        "a terrified, open-mouthed shock with glasses askew",
        "惊恐张嘴、眼镜歪斜",
    ),
    ("Morris", 7): _deny(
        "a remorseful, somber look without glasses",
        "摘下眼镜后后悔而沉重",
        "event-specific alternate appearance that unexpectedly removes his glasses",
    ),
    ("Morris", 8): _deny(
        "a weary, sad look without glasses",
        "摘下眼镜后疲惫悲伤",
        "event-specific alternate appearance that unexpectedly removes his glasses",
    ),
    ("Morris", 9): _deny(
        "a relieved, happy smile without glasses",
        "摘下眼镜后释然而开心地笑",
        "event-specific alternate appearance that unexpectedly removes his glasses",
    ),
    ("Olivia", 6): _allow(
        "a poised, pleasant, near-neutral look",
        "从容温和、近乎中性的神情",
    ),
    ("Olivia", 7): _allow(
        "a wide-eyed, startled gasp",
        "睁大眼、受惊倒吸气",
    ),
    ("Olivia", 8): _allow(
        "a delighted, grateful smile with a hand to her cheek",
        "欣喜感激地微笑并手抚脸颊",
    ),
    ("Olivia", 9): _allow(
        "a pleased, contented, eyes-closed smile",
        "闭眼愉悦满足地微笑",
    ),
    ("Scarlett", 6): _allow(
        "a surprised, excited, open-mouthed reaction",
        "惊讶兴奋地张嘴",
    ),
    ("Scarlett", 7): _allow(
        "a worried, uncertain look",
        "担忧迟疑",
    ),
    ("Sophia", 6): _allow(
        "a sad, anxious look while glancing aside",
        "悲伤焦虑地侧看",
    ),
    ("Sophia", 7): _allow(
        "an open-mouthed, distressed outcry",
        "痛苦地张嘴喊出声",
    ),
    ("Sophia", 8): _allow(
        "a joyful, eyes-closed laugh",
        "闭眼开心大笑",
    ),
    ("Sophia", 9): _allow(
        "a contented, relieved, eyes-closed smile",
        "闭眼满足释然地微笑",
    ),
    ("Sophia", 10): _allow(
        "a downcast, worried look",
        "低落忧虑",
    ),
    ("Sophia", 11): _allow(
        "a shocked, alarmed, open-mouthed reaction",
        "震惊惊慌地张嘴",
    ),
    ("Sophia", 12): _allow(
        "a deeply sad, withdrawn look",
        "深度悲伤而退缩",
    ),
    ("Sophia", 13): _allow(
        "crying with tears streaming down her face",
        "泪流满面地哭",
    ),
    ("Sophia", 14): _deny(
        "a neutral look in a Journey of the Prairie King cosplay",
        "穿草原之王旅程角色扮演服时平静",
        "cosplay-only frame embedded in the general portrait sheet",
    ),
    ("Sophia", 15): _deny(
        "a happy smile in a Journey of the Prairie King cosplay",
        "穿草原之王旅程角色扮演服时开心地笑",
        "cosplay-only frame embedded in the general portrait sheet",
    ),
    ("Sophia", 16): _deny(
        "an anxious look in a Journey of the Prairie King cosplay",
        "穿草原之王旅程角色扮演服时焦虑",
        "cosplay-only frame embedded in the general portrait sheet",
    ),
    ("Sophia", 17): _deny(
        "a stern, in-character look in a Journey of the Prairie King cosplay",
        "穿草原之王旅程角色扮演服时严肃入戏",
        "cosplay-only frame embedded in the general portrait sheet",
    ),
    ("Sophia", 18): _deny(
        "a bashful smile in a Journey of the Prairie King cosplay",
        "穿草原之王旅程角色扮演服时害羞地笑",
        "cosplay-only frame embedded in the general portrait sheet",
    ),
    ("Sophia", 19): _deny(
        "a cheerful, eyes-closed smile in a Journey of the Prairie King cosplay",
        "穿草原之王旅程角色扮演服时闭眼开心地笑",
        "cosplay-only frame embedded in the general portrait sheet",
    ),
    ("Victor", 7): _allow(
        "a surprised, anxious, open-mouthed reaction",
        "惊讶焦虑地张嘴",
    ),
    ("Victor", 8): _allow(
        "a warm, happy smile",
        "温暖开心地微笑",
    ),
    ("Victor", 9): _allow(
        "an excited, startled, open-mouthed reaction",
        "兴奋又惊讶地张嘴",
    ),
    ("Victor", 10): _allow(
        "a sad, worried look",
        "悲伤担忧",
    ),
    ("Victor", 11): _allow(
        "a gentle, relieved slight smile",
        "温和释然地浅笑",
    ),
}


EXTRA_FRAME_SEMANTICS: dict[tuple[str, str, int], dict[str, str]] = {
    (kind, npc, index): dict(semantic)
    for (npc, index), semantic in _COMMON_EXTRA_FRAME_SEMANTICS.items()
    for kind in ("sve", "seasonal")
}

EXTRA_FRAME_SEMANTICS.update({
    ("sve", "Junimos", 6): _deny(
        "an orange Junimo color variant, not an expression",
        "橙色祝尼魔颜色变体，并非表情",
        "color/species variant with no distinct emotion",
    ),
    ("sve", "Junimos", 7): _BLANK,
    ("sve", "Mermaid", 6): _BLANK,
    ("sve", "Mermaid", 7): _BLANK,
    ("sve", "Victor", 6): _allow(
        "a thoughtful, hesitant look",
        "思索迟疑",
    ),
    ("seasonal", "Victor", 6): _deny(
        "an exact duplicate of the neutral default frame",
        "与默认中性帧完全重复",
        "exact duplicate marker in most Seasonal Cute SVE assets; use $0 instead",
    ),
    ("seasonal", "ScarlettFake", 6): _allow(
        "a surprised, excited, open-mouthed reaction",
        "惊讶兴奋地张嘴",
    ),
    ("seasonal", "ScarlettFake", 7): _allow(
        "a worried, uncertain look",
        "担忧迟疑",
    ),
    ("addon", "Magnus", 6): _deny(
        "an alternate pink-haired witch appearance",
        "粉色头发女巫的替代外观",
        "the frame changes the character's hair, hat, outfit, and pose instead of expressing a reusable emotion",
    ),
    ("addon", "Magnus", 7): _BLANK,
    ("addon", "Mermaid", 6): _BLANK,
    ("addon", "Mermaid", 7): _BLANK,
    ("addon", "Olivia", 6): _allow(
        "a calm, slightly tired side glance",
        "平静而略显疲惫的侧目",
    ),
    ("addon", "Olivia", 7): _allow(
        "wide-eyed surprise and alarm",
        "睁大眼睛的惊讶与警觉",
    ),
    ("addon", "Olivia", 8): _allow(
        "a blushing, warmly touched look with a hand to her chest",
        "脸红、手抚胸口而深受感动的神情",
    ),
    ("addon", "Olivia", 9): _allow(
        "an eyes-closed, warmly flirtatious smile",
        "闭眼而温暖暧昧的微笑",
    ),
    ("addon", "Sophia", 6): _allow(
        "an upset, tearful look",
        "难过而含泪的神情",
    ),
    ("addon", "Sophia", 7): _allow(
        "a distressed, open-mouthed crying look",
        "痛苦地张嘴哭泣的神情",
    ),
    ("addon", "Sophia", 8): _allow(
        "a playful, musical smile",
        "带音符的俏皮微笑",
    ),
    ("addon", "Sophia", 9): _allow(
        "a cheerful musical wink",
        "带音符的开心眨眼",
    ),
    ("addon", "Sophia", 10): _allow(
        "a subdued, vulnerable look",
        "低落而脆弱的神情",
    ),
    ("addon", "Sophia", 11): _allow(
        "wide-eyed surprise",
        "睁大眼睛的惊讶",
    ),
    ("addon", "Sophia", 12): _allow(
        "a downcast, openly tearful look",
        "低头而明显落泪的神情",
    ),
    ("addon", "Sophia", 13): _allow(
        "a tearful but grateful smile",
        "含泪而感激的微笑",
    ),
    **{
        ("addon", "Sophia", index): _deny(
            english,
            chinese,
            "the frame switches to a scene-specific cowgirl cosplay instead of only changing expression",
        )
        for index, english, chinese in (
            (14, "a neutral cowgirl-cosplay pose", "中性的牛仔女郎装扮姿势"),
            (15, "a bright cowgirl-cosplay smile", "牛仔女郎装扮下的明快微笑"),
            (16, "an anxious cowgirl-cosplay look", "牛仔女郎装扮下的不安神情"),
            (17, "a stern cowgirl-cosplay look", "牛仔女郎装扮下的严肃神情"),
            (18, "a shy cowgirl-cosplay smile", "牛仔女郎装扮下的害羞微笑"),
            (19, "a cheerful cowgirl-cosplay wink", "牛仔女郎装扮下的开心眨眼"),
        )
    },
})


EXTRA_FRAME_ASSET_OVERRIDES: dict[
    tuple[str, str, int, str],
    dict[str, str],
] = {
    # Andy's sixth frame is a prop-free weary look only in these variants.
    ("sve", "Andy", 6, "andy/andy_beach.png"): _allow(
        "a weary, eyes-closed look",
        "疲惫地闭眼",
    ),
    ("seasonal", "Andy", 6, "andy/andy_beach.png"): _allow(
        "a weary, eyes-closed look",
        "疲惫地闭眼",
    ),
    ("seasonal", "Andy", 6, "andy/andy_spring.png"): _allow(
        "a weary, eyes-closed look",
        "疲惫地闭眼",
    ),
    ("seasonal", "Andy", 6, "andy/andy_spiritseve.png"): _allow(
        "a weary, eyes-closed look",
        "疲惫地闭眼",
    ),
    ("seasonal", "Andy", 7, "andy/andy_spiritseve.png"): _deny(
        "an exact duplicate of frame 4 in the same portrait",
        "与同一肖像的第 4 帧完全重复",
        "exact duplicate marker in the Spirit's Eve asset",
    ),
    # These dedicated Claire assets pad their unused costume slots with blanks.
    **{
        ("sve", "Claire", index, asset): _BLANK
        for asset in ("claire/claire_beach.png", "claire/claire_theater.png")
        for index in range(8, 16)
    },
    **{
        ("seasonal", "Claire", index, "claire/claire_beach.png"): _BLANK
        for index in range(8, 16)
    },
    **{
        ("seasonal", "Claire", index, "claire/claire_theater.png"): _BLANK
        for index in range(8, 10)
    },
    # Dedicated beach/dance sheets do not contain Lance's shirtless event set.
    **{
        ("sve", "Lance", index, "lance/lance_beach.png"): _BLANK
        for index in range(7, 10)
    },
    **{
        ("seasonal", "Lance", index, asset): _BLANK
        for asset in ("lance/lance_beach.png", "lance/lance_flowerdance.png")
        for index in range(7, 10)
    },
    # The cosplay placeholders contain no drawable Scarlett portrait data.
    **{
        (kind, "Scarlett", index, "scarlett/scarlett_cosplay.png"): _BLANK
        for kind in ("sve", "seasonal")
        for index in range(6, 10)
    },
    # These are CP edit layers. The generator audits their final composites.
    **{
        ("seasonal", "Sophia", index, asset): _PARTIAL_OVERLAY
        for asset in (
            "sophia/sophia_older_overlay.png",
            "sophia/sophia_older_mu_overlay.png",
        )
        for index in range(6, 20)
    },
    ("seasonal", "Victor", 6, "victor/victor_summer.png"): _allow(
        "a thoughtful, hesitant look",
        "思索迟疑",
    ),
    # These two pixels are also SVE's reviewed thoughtful frame 6.  Treating
    # their Seasonal copies as duplicate-only would disable the shared hash.
    ("seasonal", "Victor", 6, "victor/victor_beach.png"): _allow(
        "a thoughtful, hesitant look",
        "思索迟疑",
    ),
    ("seasonal", "Victor", 6, "victor/victor_winter_outdoor.png"): _allow(
        "a thoughtful, hesitant look",
        "思索迟疑",
    ),
    # This Seasonal sheet reuses the exact OhoDavi addon pixels, whose props
    # and tears make the narrower descriptions more accurate than the broad
    # SVE slot defaults.
    ("seasonal", "Sophia", 6, "sophia/sophia_summer .png"): _allow(
        "an upset, tearful look",
        "难过而含泪的神情",
    ),
    ("seasonal", "Sophia", 7, "sophia/sophia_summer .png"): _allow(
        "a distressed, open-mouthed crying look",
        "痛苦地张嘴哭泣的神情",
    ),
    ("seasonal", "Sophia", 8, "sophia/sophia_summer .png"): _allow(
        "a playful, musical smile",
        "带音符的俏皮微笑",
    ),
    ("seasonal", "Sophia", 9, "sophia/sophia_summer .png"): _allow(
        "a cheerful musical wink",
        "带音符的开心眨眼",
    ),
    ("seasonal", "Sophia", 10, "sophia/sophia_summer .png"): _allow(
        "a subdued, vulnerable look",
        "低落而脆弱的神情",
    ),
    ("seasonal", "Sophia", 11, "sophia/sophia_summer .png"): _allow(
        "wide-eyed surprise",
        "睁大眼睛的惊讶",
    ),
    ("seasonal", "Sophia", 12, "sophia/sophia_summer .png"): _allow(
        "a downcast, openly tearful look",
        "低头而明显落泪的神情",
    ),
    ("seasonal", "Sophia", 13, "sophia/sophia_summer .png"): _allow(
        "a tearful but grateful smile",
        "含泪而感激的微笑",
    ),
    ("seasonal", "Olivia", 6, "olivia/olivia_beach.png"): _allow(
        "a composed, mildly skeptical side glance",
        "从容而略带审视的侧目",
    ),
    ("seasonal", "Olivia", 7, "olivia/olivia_beach.png"): _allow(
        "a startled sideways glance",
        "受惊地侧目",
    ),
    ("seasonal", "Olivia", 8, "olivia/olivia_beach.png"): _allow(
        "a blushing, warmly pleased smile",
        "脸红而温暖愉悦的微笑",
    ),
    ("seasonal", "Olivia", 9, "olivia/olivia_beach.png"): _allow(
        "an eyes-closed, contented smile",
        "闭眼而满足的微笑",
    ),
    ("addon", "Olivia", 6, "olivia_beach.png"): _allow(
        "a composed, mildly skeptical side glance",
        "从容而略带审视的侧目",
    ),
    ("addon", "Olivia", 7, "olivia_beach.png"): _allow(
        "a startled sideways glance",
        "受惊地侧目",
    ),
    ("addon", "Olivia", 8, "olivia_beach.png"): _allow(
        "a blushing, warmly pleased smile",
        "脸红而温暖愉悦的微笑",
    ),
    ("addon", "Olivia", 9, "olivia_beach.png"): _allow(
        "an eyes-closed, contented smile",
        "闭眼而满足的微笑",
    ),
}


_RASMODIA_RED_HAIR = _deny(
    "an alternate red-haired magical appearance",
    "红发魔法外观变体",
    "the frame is a story-specific magical transformation that changes appearance rather than only expression",
)
_RASMODIA_TEARFUL_PAIN = _allow(
    "an openly tearful, pained look",
    "明显落泪而痛苦的神情",
)


def _register_asset_semantics(
    kind: str,
    npc: str,
    assets: tuple[str, ...],
    frames: dict[int, dict[str, str]],
) -> None:
    for asset in assets:
        for index, semantic in frames.items():
            EXTRA_FRAME_ASSET_OVERRIDES[(kind, npc, index, asset)] = semantic


# Romanceable Rasmodia selects one of several independent portrait artists at
# runtime.  Keep those meanings per file: the numeric slots are not consistent
# enough to describe safely with one broad Wizard/Magnus table.
_register_asset_semantics(
    "rasmodia",
    "Wizard",
    (
        "creepykat's/witch_beach.png",
        "creepykat's/witch_flowerdance.png",
        "creepykat's/witch_nonsve.png",
    ),
    {
        6: _allow("wide-eyed, open-mouthed surprise", "睁大眼睛、张嘴惊讶"),
        7: _allow("a gentle, relieved smile", "温和而如释重负的微笑"),
        8: _allow("a tearful, bittersweet smile", "含泪而苦涩的微笑"),
    },
)
_register_asset_semantics(
    "rasmodia",
    "Magnus",
    ("creepykat's/witch_sve.png",),
    {
        6: _RASMODIA_RED_HAIR,
        7: _allow("a quiet, subdued look", "安静而低落的神情"),
        8: _allow("a tearful, pained look", "含泪而痛苦的神情"),
    },
)

_register_asset_semantics(
    "rasmodia",
    "Wizard",
    (
        "dacar's/witch_nonsve.png",
        "dacar's/hatless/witch_nonsve.png",
    ),
    {
        6: _allow("wide-eyed surprise", "睁大眼睛的惊讶"),
        7: _allow("a shy, faint smile", "害羞而淡淡的微笑"),
        8: _RASMODIA_TEARFUL_PAIN,
    },
)
_register_asset_semantics(
    "rasmodia",
    "Magnus",
    (
        "dacar's/witch_sve.png",
        "dacar's/hatless/witch_sve.png",
    ),
    {
        6: _RASMODIA_RED_HAIR,
        7: _allow("a quiet, faint smile", "安静而淡淡的微笑"),
        8: _RASMODIA_TEARFUL_PAIN,
    },
)
_register_asset_semantics(
    "rasmodia",
    "Magnus",
    (
        "dacar's/witch_sve_romras_12.png",
        "dacar's/hatless/witch_sve_romras_12.png",
    ),
    {
        6: _allow("a bashful, affectionate look", "害羞而亲昵的神情"),
        7: _allow("a bright, open smile", "明亮而开朗的微笑"),
        8: _RASMODIA_TEARFUL_PAIN,
        9: _allow("an eyes-closed, contented smile", "闭眼而满足的微笑"),
        10: _allow("a tired, uneasy look", "疲惫而不安的神情"),
        11: _allow("a guarded, resolute look", "戒备而坚定的神情"),
        12: _RASMODIA_RED_HAIR,
    },
)

_register_asset_semantics(
    "rasmodia",
    "Wizard",
    (
        "nyapu/witch_nonsve.png",
        "nyapu/hatless/witch_nonsve.png",
    ),
    {
        6: _allow(
            "a startled, open-mouthed look with one hand raised",
            "张嘴受惊并抬手的神情",
        ),
        7: _allow(
            "a warm smile with one hand over her heart",
            "手抚心口的温暖微笑",
        ),
        8: _allow(
            "a worried, vulnerable look with one hand near her face",
            "手靠近脸颊、担心而脆弱的神情",
        ),
    },
)
_register_asset_semantics(
    "rasmodia",
    "Magnus",
    (
        "nyapu/witch_sve.png",
        "nyapu/hatless/witch_sve.png",
    ),
    {
        6: _RASMODIA_RED_HAIR,
        7: _allow(
            "a subdued, uneasy look with one hand over her heart",
            "手抚心口、低落而不安的神情",
        ),
        8: _allow(
            "a tearful, downcast look with one hand over her heart",
            "手抚心口、含泪而低落的神情",
        ),
    },
)
_register_asset_semantics(
    "rasmodia",
    "Magnus",
    (
        "nyapu/witch_sve_romras_12.png",
        "nyapu/hatless/witch_sve_romras_12.png",
    ),
    {
        6: _allow(
            "a bashful, affectionate smile with one hand near her face",
            "手靠近脸颊、害羞而亲昵的微笑",
        ),
        # Hatless frame 7 shares its exact runtime key with Hatless non-SVE
        # frame 7, so both use one truthful description instead of conflicting.
        7: _allow(
            "a warm smile with one hand over her heart",
            "手抚心口的温暖微笑",
        ),
        8: _allow(
            "a tearful, downcast look with one hand over her heart",
            "手抚心口、含泪而低落的神情",
        ),
        9: _allow(
            "an eyes-closed, contented smile with one hand over her heart",
            "闭眼、手抚心口的满足微笑",
        ),
        10: _allow(
            "a tired, uneasy look with one hand over her heart",
            "手抚心口、疲惫而不安的神情",
        ),
        11: _allow(
            "a guarded side glance with one hand near her face",
            "手靠近脸颊、戒备地侧目",
        ),
        12: _RASMODIA_RED_HAIR,
    },
)

_register_asset_semantics(
    "rasmodia",
    "Wizard",
    ("original/witch_nonsve.png",),
    {
        6: _allow("wide-eyed, open-mouthed surprise", "睁大眼睛、张嘴惊讶"),
        7: _allow("a gentle, relieved smile", "温和而如释重负的微笑"),
        8: _RASMODIA_TEARFUL_PAIN,
    },
)
_register_asset_semantics(
    "rasmodia",
    "Magnus",
    ("original/witch_sve.png",),
    {
        6: _RASMODIA_RED_HAIR,
        7: _allow("a gentle, relieved smile", "温和而如释重负的微笑"),
        8: _RASMODIA_TEARFUL_PAIN,
    },
)
_register_asset_semantics(
    "rasmodia",
    "Magnus",
    ("original/witch_sve_romras_12.png",),
    {
        6: _allow("an eyes-closed, affectionate smile", "闭眼而亲昵的微笑"),
        7: _allow("a bright, open smile", "明亮而开朗的微笑"),
        8: _RASMODIA_TEARFUL_PAIN,
        9: _allow("an eyes-closed, contented smile", "闭眼而满足的微笑"),
        10: _allow("a tired, uneasy look", "疲惫而不安的神情"),
        11: _allow("a guarded, resolute look", "戒备而坚定的神情"),
        12: _RASMODIA_RED_HAIR,
    },
)
