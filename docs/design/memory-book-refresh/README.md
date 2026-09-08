# 记忆手册像素素材更新

保留棕色皮革、羊皮纸和蜂蜜金色。标题丝带去掉白色点纹衬板，改用干净的金色；
“记忆手册”和 “MEMORY BOOK” 使用生成器内手绘点阵字，不依赖系统字体。
深绿藤蔓、淡杏金花和蓝金蝴蝶只点缀皮革外框。

图集为 `LivingNPCs/assets/ui/memory-book.png`，尺寸从 256 × 128 扩展到
256 × 192。既有九宫格、普通图标、红绳约定结的源坐标保持不变。

| API | 源矩形 (x, y, w, h) | 建议显示尺寸 |
| --- | --- | --- |
| `MemoryBookIcon.SealedNote` | (80, 80, 16, 16) | 32 × 32，与“关于你们”标题居中对齐 |
| `TitleChineseSource` | (0, 104, 72, 20) | 216 × 60，放在丝带正中 |
| `TitleEnglishSource` | (80, 104, 92, 20) | 276 × 60，放在丝带正中 |
| `VineCornerSource` | (0, 128, 40, 40) | 80 × 80，左上；旋转 180° 用于右下 |
| `ButterflySource` | (48, 128, 24, 20) | 48 × 40，顶部远离关闭按钮处 |

所有新图使用透明底和整数像素。藤蔓源图仅占顶部 10 行或左侧 10 列，
朝向纸页的 30 × 30 区域完全透明。标题缩放不足时应选择更小的整数倍；
其他语言仍使用游戏原有字体显示翻译标题。

封蜡约定笺直接读取 `docs/design/memory-book-promise-icon/sealed-note.json`
的批准原稿；“约定”继续使用 `MemoryBookIcon.Promise` 红绳结。

重新生成图集：

```powershell
python tools/generate_memory_book_ui.py
```

需要 Python 和 Pillow。放大预览应使用最近邻，不应保存经过平滑插值的图作为正式素材。
