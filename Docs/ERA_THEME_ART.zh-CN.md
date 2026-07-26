# 五年代原创卡包视觉资产

最后更新：2026-07-26

## 用途与边界

这五张图片是万能抽卡模拟器的轻量核心 UI 皮肤，用来让当前五个测试年代在进入撕包阶段时具有可辨识的包装视觉。它们不是任何真实宝可梦卡包的复制品，也不包含 Pokémon 名称、角色、官方标志、系列文字、卡背或其他现成品牌元素。

大量卡图仍保存在按系列下载的私人内容包中；只有这五张通用年代包装随 APK 发布。Android 干净构建显示它们与对应接入代码合计只增加 304,640 bytes（约 0.29 MiB）。

## 生成与导入记录

- 生成方式：Codex 内置 `imagegen`。
- 生成日期：2026-07-26。
- 源文件：1024 × 1536 PNG。
- Unity 路径：`Assets/Resources/Gacha/Themes/`。
- 运行时路径：`Gacha/Themes/<theme>-pack`。
- 导入限制：最长边 512px、关闭 mipmap、Clamp、Bilinear、压缩纹理。
- Android：ASTC 6×6、质量 75。
- 自动化：`ThemeArtworkImportProcessor` 会在重新导入时恢复以上设置，Editor 测试会核对五张图及 Android 覆盖参数。

## 最终提示词组合

每张图片使用下面的共同约束，再合并对应主题段。以下内容记录的是最终采用的提示词集合。

### 共同段

```text
Use case: stylized-concept
Asset type: in-game collectible-card booster pack front artwork, portrait composition
Scene/backdrop: flat front-facing packaging artwork fills the entire portrait canvas edge-to-edge; no tabletop, no product mockup scene
Composition/framing: portrait layout with a strong central focal point, decorative top and bottom foil bands, important details kept away from edges
Constraints: fully original; no text, letters, numbers, logos, trademarks, watermarks, QR codes, signatures, card backs, or existing intellectual-property elements; do not resemble any real booster pack; one uninterrupted artwork only
```

### Vintage / Base Set

```text
Primary request: create an original vintage-era booster wrapper visual with a warm 1990s printed-card feeling
Subject: a bold central abstract sun medallion with orbiting geometric shapes and subtle starbursts; no creatures, people, or recognizable franchise motifs
Style/medium: polished hand-printed illustration, restrained retro halftone and paper grain, premium game asset
Composition detail: symmetrical
Lighting/mood: warm nostalgic glow
Color palette: aged cream, muted burgundy, deep brown, antique gold
Materials/textures: printed metallic foil with subtle creases and ink texture
```

### Forest / Neo Genesis

```text
Primary request: create an original forest-era booster wrapper visual with an organic turn-of-the-millennium fantasy feeling
Subject: a central abstract seed crystal surrounded by layered leaves, concentric growth rings, small floating spores, and elegant botanical geometry; no creatures or people
Style/medium: polished illustrated game asset, screen-printed linework blended with luminous natural textures
Composition detail: symmetrical
Lighting/mood: mysterious moonlit woodland, calm and magical
Color palette: deep pine green, moss, muted teal, pale moon gold
Materials/textures: printed emerald foil with subtle creases, leaf-vein embossing, and fine ink grain
```

### Ruby / EX Ruby & Sapphire

```text
Primary request: create an original ruby-era booster wrapper visual with energetic early-2000s collectible-game styling
Subject: a faceted central ruby prism suspended above intersecting magma arcs, angular speed lines, and small mineral shards; no creatures or people
Style/medium: polished illustrated game asset, sharp cel-painted geometry with premium metallic print finish
Composition detail: dynamic but balanced
Lighting/mood: bold, heated, adventurous
Color palette: crimson, wine red, ember orange, charcoal, silver-white highlights
Materials/textures: glossy red metallic foil, subtle creases, spot-varnish geometry
```

### Electric / Sword & Shield

```text
Primary request: create an original electric-era booster wrapper visual with sleek late-2010s competitive card-game energy
Subject: a central abstract energy core crossed by branching lightning ribbons, layered circuit arcs, and angular luminous fragments; no creatures or people
Style/medium: polished illustrated game asset, clean high-energy digital painting with graphic neon geometry
Composition detail: dynamic layout with sweeping diagonals
Lighting/mood: charged, fast, futuristic
Color palette: cobalt blue, electric cyan, hot magenta accents, midnight navy, white sparks
Materials/textures: holographic blue foil with subtle creases, prismatic edge highlights
```

### Gallery / Scarlet & Violet

```text
Primary request: create an original gallery-era booster wrapper visual with contemporary premium collectible-art styling
Subject: a central abstract iridescent portal made from overlapping translucent ribbons, floating gallery-frame fragments, and soft geometric blooms; no creatures or people
Style/medium: polished modern editorial illustration, refined gradients and layered holographic shapes, premium game asset
Composition detail: elegant layout with asymmetric details held in visual balance
Lighting/mood: celebratory, artistic, luminous
Color palette: violet, coral, cyan, pearl white, touches of warm gold
Materials/textures: pearlescent rainbow foil with subtle creases and selective holographic sparkle
```

## 后续替换规则

1. 保持资源路径不变即可无代码替换同主题图片。
2. 新主题通过 `ProductOpeningTheme.PackArtworkResourcePath` 指定资源，不在通用控制器中判断游戏或系列 ID。
3. 若将来包装图数量明显增加，应迁入可下载内容包；当前五张合计 APK 增量只有约 0.29 MiB，因此保留为离线可用的核心皮肤。
4. 粒子光效已经由通用 `ProductOpeningParticleTheme` 与复用式 `ThemeParticleField` 驱动，不属于这些位图资产；新主题只应声明参数和 USS 配色，不在控制器中写系列分支。
5. 正式录制音效仍是后续工作；减少动态效果必须继续保留静态包装与稀有强调。
