"""Reviewed semantics for vanilla NPC portrait frames above the standard six.

The generator keeps this audit data separate from its image-discovery logic so
every numeric frame is opt-in.  ``kind`` distinguishes the unmodified game art
from the installed OhoDavi portraits.  Asset overrides are deliberately narrow:
they only apply when the normalized source path contains the given substring.
"""

from __future__ import annotations


def _allow(en: str, zh: str) -> dict[str, str]:
    return {"decision": "allow", "en": en, "zh": zh}


def _deny(en: str, zh: str, reason: str) -> dict[str, str]:
    return {"decision": "deny", "en": en, "zh": zh, "reason": reason}


# Entries shared by vanilla and OhoDavi.  Source-specific disagreements are
# appended below instead of being obscured by a broad default.
_COMMON_EXTRA_FRAME_SEMANTICS: dict[tuple[str, int], dict[str, str]] = {
    ("Abigail", 6): _allow(
        "a subdued, uneasy look with underlying concern",
        "低落、局促而暗含担心",
    ),
    ("Abigail", 7): _allow(
        "a startled, open-mouthed gasp",
        "睁大眼、张嘴倒吸气的惊吓",
    ),
    ("Abigail", 8): _allow(
        "a quiet, tentative, reflective look",
        "安静、试探而若有所思",
    ),
    ("Abigail", 9): _allow(
        "an awkward, guarded, slightly embarrassed look",
        "局促、戒备而略显尴尬",
    ),
    ("Alex", 6): _deny(
        "a shirtless, confident smile",
        "赤裸上身、自信地微笑",
        "special shirtless costume; unsafe as a general-purpose expression",
    ),
    ("Alex", 7): _allow(
        "a startled, hurt, open-mouthed reaction",
        "受惊又受伤、张嘴愕然",
    ),
    ("Alex", 8): _deny(
        "a shirtless, subdued, apologetic look",
        "赤裸上身、低落而歉疚的神情",
        "special shirtless costume; unsafe as a general-purpose expression",
    ),
    ("Alex", 9): _allow(
        "a somber, guarded, reflective look",
        "低沉、戒备而若有所思",
    ),
    ("Alex", 10): _deny(
        "eating and savoring food",
        "正在进食并品尝食物",
        "food prop and eating action; not a general-purpose expression",
    ),
    ("Alex", 11): _deny(
        "a solid or transparent placeholder tile",
        "纯色或透明占位帧",
        "placeholder tile with no drawable facial expression",
    ),
    ("Clint", 6): _allow(
        "a panicked, anxious outburst",
        "慌张焦虑、近乎失控",
    ),
    ("Clint", 7): _allow(
        "a dejected, resigned, downcast look",
        "沮丧、认命而低落",
    ),
    ("Demetrius", 6): _allow(
        "a wide-eyed, alarmed gasp",
        "睁大眼、惊慌倒吸气",
    ),
    ("Demetrius", 7): _deny(
        "wearing a hazmat suit and respirator",
        "穿着防化服并戴着呼吸器",
        "special hazmat costume and respirator prop",
    ),
    ("Elliott", 7): _allow(
        "a subdued, worried, reflective look",
        "低沉、担忧而若有所思",
    ),
    ("Elliott", 8): _allow(
        "a wide-eyed, flustered surprise",
        "睁大眼、张嘴慌乱吃惊",
    ),
    ("Elliott", 9): _deny(
        "a transparent placeholder tile",
        "透明占位帧",
        "fully transparent placeholder with no drawable expression",
    ),
    ("Emily", 6): _allow(
        "a startled, frightened gasp",
        "受惊害怕、张嘴倒吸气",
    ),
    ("Emily", 7): _deny(
        "eyes closed while holding a glowing golden object",
        "闭着眼、手持发光的金色物体",
        "glowing golden prop and event-specific pose",
    ),
    ("Haley", 6): _allow(
        "a calm, pleasant half-smile",
        "平静愉快的浅笑",
    ),
    ("Haley", 7): _allow(
        "a cool, displeased, distant look",
        "冷淡、不悦而疏离",
    ),
    ("Haley", 8): _allow(
        "wide-eyed surprise",
        "睁大眼、张嘴惊讶",
    ),
    ("Haley", 9): _deny(
        "mud-smeared distress",
        "满身泥污、痛苦狼狈",
        "event-specific mud-covered costume and pose",
    ),
    ("Haley", 10): _deny(
        "a mud-smeared, delighted laugh",
        "满身泥污、开心大笑",
        "event-specific mud-covered costume and pose",
    ),
    ("Haley", 11): _allow(
        "tender, romantic contentment with closed eyes",
        "闭眼、温柔而亲昵满足",
    ),
    ("Haley", 12): _allow(
        "wide-eyed fear and anxiety",
        "睁大眼的害怕与焦虑",
    ),
    ("Haley", 13): _allow(
        "a coy, confident, flirtatious half-smile",
        "自信、俏皮而带调情意味的浅笑",
    ),
    ("Harvey", 7): _allow(
        "a flustered, anxious look",
        "慌乱焦虑",
    ),
    ("Harvey", 8): _allow(
        "wide-eyed alarm with an exclamation mark",
        "睁大眼、带惊叹号的惊慌",
    ),
    ("Harvey", 9): _deny(
        "calmly wearing radio headphones",
        "平静地戴着无线电耳机",
        "radio-headphone prop and event-specific pose",
    ),
    ("Harvey", 10): _deny(
        "startled while wearing radio headphones",
        "戴着无线电耳机时受惊",
        "radio-headphone prop and event-specific pose",
    ),
    ("Krobus", 6): _allow(
        "a sharp-toothed, mischievous, slightly sinister grin",
        "露尖牙、顽皮又略显阴森的咧嘴笑",
    ),
    ("Krobus", 8): _deny(
        "a human disguise with a hat and eyewear",
        "戴帽子和眼镜的人类伪装",
        "special human-disguise costume with hat and eyewear props",
    ),
    ("Krobus", 9): _deny(
        "a transparent placeholder tile",
        "透明占位帧",
        "fully transparent placeholder with no drawable expression",
    ),
    ("Leah", 6): _allow(
        "a startled, embarrassed gasp with a hand near the mouth",
        "受惊又尴尬、手靠近嘴边",
    ),
    ("Leah", 7): _allow(
        "a hesitant, thoughtful, uncertain look",
        "迟疑、思索而不确定",
    ),
    ("Leah", 8): _deny(
        "angrily speaking on a telephone",
        "生气地对着电话说话",
        "telephone prop and event-specific speaking pose",
    ),
    ("Leah", 9): _deny(
        "a transparent placeholder tile",
        "透明占位帧",
        "fully transparent placeholder with no drawable expression",
    ),
    ("Maru", 6): _allow(
        "a calm, restrained, near-neutral smile",
        "平静克制、近乎中性的浅笑",
    ),
    ("Maru", 7): _allow(
        "a soft, friendly slight smile",
        "柔和友善的浅笑",
    ),
    ("Maru", 8): _allow(
        "a quiet, slightly uneasy, reflective look",
        "安静、略显不安而若有所思",
    ),
    ("Maru", 9): _allow(
        "a wide-eyed, alarmed gasp",
        "睁大眼、带惊叹号的惊慌",
    ),
    ("Penny", 6): _deny(
        "a shy, tender smile in a swimsuit",
        "穿泳装时害羞温柔地微笑",
        "special swimsuit costume; unsafe outside the beach context",
    ),
    ("Penny", 7): _allow(
        "a gentle, grateful smile",
        "温柔感激的微笑",
    ),
    ("Penny", 8): _deny(
        "a blushing confession in a swimsuit",
        "穿泳装时脸红告白",
        "special swimsuit costume and event-specific confession pose",
    ),
    ("Penny", 9): _deny(
        "an angry rejection in a swimsuit",
        "穿泳装时生气拒绝",
        "special swimsuit costume and event-specific rejection pose",
    ),
    ("Penny", 10): _deny(
        "tearful anguish in a swimsuit with loosened hair",
        "穿泳装、散开头发并痛苦落泪",
        "special swimsuit costume, loosened hair, and event-specific distress",
    ),
    ("Penny", 11): _allow(
        "a warm, content, open smile",
        "温暖满足、坦然的微笑",
    ),
    ("Penny", 12): _allow(
        "nervous, wide-eyed surprise",
        "紧张、睁大眼的受惊神情",
    ),
    ("Penny", 13): _deny(
        "a transparent placeholder tile",
        "透明占位帧",
        "fully transparent placeholder with no drawable expression",
    ),
    ("Robin", 6): _allow(
        "a dry, skeptical, mildly annoyed grimace",
        "带讽刺感、怀疑而略恼的勉强表情",
    ),
    ("Robin", 7): _deny(
        "a solid or transparent placeholder tile",
        "纯色或透明占位帧",
        "placeholder tile with no drawable facial expression",
    ),
    ("Sam", 6): _deny(
        "reading and holding a letter",
        "手持并阅读信件",
        "letter prop and event-specific reading pose",
    ),
    ("Sam", 7): _allow(
        "an earnest, gentle, reserved smile",
        "真诚、温和而克制的浅笑",
    ),
    ("Sam", 8): _allow(
        "a startled, alarmed look",
        "睁大眼的受惊慌张",
    ),
    ("Sam", 9): _allow(
        "a worried, sad, downcast look",
        "担忧、难过而低落",
    ),
    ("Sam", 10): _allow(
        "an eyes-closed, bashful grin with a hand behind the neck",
        "闭眼、手挠后颈的害羞大笑",
    ),
    ("Sam", 11): _deny(
        "an uncanny expression against a Spirit's Eve dark background",
        "万灵节深色背景下的诡异表情",
        "special Spirit's Eve background and event-specific presentation",
    ),
    ("Sebastian", 6): _deny(
        "an exact duplicate of the neutral default frame",
        "与默认中性帧完全重复",
        "exact duplicate marker; use $0 instead",
    ),
    ("Sebastian", 7): _allow(
        "a warm, quiet, friendly smile",
        "温暖安静、友善的浅笑",
    ),
    ("Sebastian", 8): _deny(
        "smiling against a motorcycle-garage background",
        "在摩托车车库背景前微笑",
        "special motorcycle/garage background and event-specific pose",
    ),
    ("Sebastian", 9): _deny(
        "a wistful look against a motorcycle-garage background",
        "在摩托车车库背景前流露惆怅",
        "special motorcycle/garage background and event-specific pose",
    ),
    ("Shane", 6): _allow(
        "a calm, quietly hopeful half-smile",
        "平静、隐约带希望的浅笑",
    ),
    ("Shane", 7): _deny(
        "a collapsed, intoxicated, unconscious pose",
        "醉倒并失去意识的姿势",
        "event-specific collapsed/intoxicated full pose",
    ),
    ("Shane", 8): _deny(
        "delighted while holding a chicken",
        "开心地抱着鸡",
        "chicken prop and event-specific holding pose",
    ),
    ("Shane", 9): _deny(
        "serious while holding a chicken",
        "严肃地抱着鸡",
        "chicken prop and event-specific holding pose",
    ),
    ("Shane", 10): _allow(
        "a wide-eyed, startled, nervous look",
        "睁大眼、受惊紧张",
    ),
    ("Shane", 11): _deny(
        "a solid black placeholder tile",
        "纯黑占位帧",
        "solid black placeholder with no drawable expression",
    ),
}


EXTRA_FRAME_SEMANTICS: dict[tuple[str, str, int], dict[str, str]] = {
    (kind, npc, index): dict(semantic)
    for (npc, index), semantic in _COMMON_EXTRA_FRAME_SEMANTICS.items()
    for kind in ("vanilla", "oho")
}

EXTRA_FRAME_SEMANTICS.update({
    ("vanilla", "Elliott", 6): _allow(
        "a squinting, disgusted, repulsed grimace",
        "眯眼、厌恶反胃的龇牙表情",
    ),
    ("oho", "Elliott", 6): _deny(
        "an exact duplicate of the neutral frame",
        "与中性帧完全重复",
        "exact duplicate marker in the OhoDavi portrait; use $0 instead",
    ),
    ("vanilla", "Harvey", 6): _deny(
        "an exact duplicate of the neutral frame",
        "与中性帧完全重复",
        "exact duplicate marker in the vanilla portrait; use $0 instead",
    ),
    ("oho", "Harvey", 6): _allow(
        "a calm, gentle, composed smile",
        "平静、温和而从容的浅笑",
    ),
    ("vanilla", "Harvey", 11): _deny(
        "an exact duplicate of the neutral frame",
        "与中性帧完全重复",
        "exact duplicate marker in the vanilla portrait; use $0 instead",
    ),
    ("oho", "Harvey", 11): _allow(
        "a reserved, self-conscious, earnest look",
        "克制、难为情而真诚",
    ),
    ("vanilla", "Krobus", 7): _allow(
        "a worried, downcast frown",
        "担心低落的小皱脸",
    ),
    ("oho", "Krobus", 7): _allow(
        "an eyes-squeezed, flustered, exuberant toothy laugh",
        "闭紧眼、慌乱又夸张的露齿大笑",
    ),
})


EXTRA_FRAME_ASSET_OVERRIDES: dict[
    tuple[str, str, int, str],
    dict[str, str],
] = {
    ("oho", "Abigail", 6, "abigail/beach.png"): _deny(
        "an exact duplicate of the neutral frame",
        "与中性帧完全重复",
        "exact duplicate marker in the OhoDavi beach asset; use $0 instead",
    ),
    ("oho", "Abigail", 6, "abigail/abigail_winter.png"): _deny(
        "an exact duplicate of the neutral frame",
        "与中性帧完全重复",
        "exact duplicate marker in the OhoDavi winter asset; use $0 instead",
    ),
    ("oho", "Abigail", 8, "abigail/glasses.png"): _deny(
        "brushing her teeth with a blue toothbrush",
        "正用蓝色牙刷刷牙",
        "toothbrush prop and event-specific brushing action",
    ),
    ("oho", "Alex", 6, "alex_winter.png"): _allow(
        "a winter-clothed, mildly confident smile",
        "穿冬装、略显自信地微笑",
    ),
    ("oho", "Alex", 8, "alex_winter.png"): _allow(
        "a winter-clothed, subdued, apologetic look",
        "穿冬装、低落而歉疚的神情",
    ),
    ("oho", "Haley", 6, "haley/beach.png"): _deny(
        "an exact duplicate of the neutral frame",
        "与中性帧完全重复",
        "exact duplicate marker in the OhoDavi beach asset; use $0 instead",
    ),
    ("oho", "Haley", 6, "haley/haley_winter.png"): _deny(
        "an exact duplicate of the neutral frame",
        "与中性帧完全重复",
        "exact duplicate marker in the OhoDavi winter asset; use $0 instead",
    ),
    ("vanilla", "Sam", 9, "sam_winter.png"): _deny(
        "a worried look that unexpectedly removes the winter hat",
        "担忧低落，但会突然摘掉冬帽",
        "outfit discontinuity: the frame reuses non-winter art and removes the winter hat",
    ),
}
