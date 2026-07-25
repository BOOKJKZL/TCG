# 卡包规则来源与可信度

最后更新：2026-07-25

本文件记录每一份 `HistoricallyVerified` 或 `SourceInformedSimulation` 规则的证据、实现边界和不能推断的内容。没有列在这里的产品必须继续标记为 `Simulated`。

## Base Set Unlimited（英文）

规则 Profile：`pokemon-base1-unlimited-empirical-v1`

实现范围：

- 5 张非能量 Common。
- 2 张 Common Basic Energy。
- 3 张 Uncommon；Double Colorless Energy 保留在 Uncommon 卡池。
- 1 张 Rare；平均约三包一张 Holo Rare。
- 同一个卡槽池内不产生重复 Printing。
- 只使用英文、非 First Edition 的印刷版本。
- Machamp #8 不进入 booster 的 Holo Rare 卡池。

来源：

1. Mark Stamp 与 Ethan Le 的实包研究：<https://www.cs.sjsu.edu/~stamp/cv/papers/pokemon.pdf>
   - 样本为 153 包 Base Set（论文称 “base one”）Unlimited、每包 11 张。
   - 论文记录每包 5 Common、2 Common Energy、3 Uncommon、1 Rare。
   - 样本中 Holofoil 平均约每三包一张。
2. PokéBeach Base Set Theme Deck 档案：<https://www.pokebeach.com/tcg/base-set/theme-decks>
   - Machamp 只来自 Starter 产品，不能从 Base Set booster 获得。
   - 没有正式发行的 Unlimited Base Set Machamp，因此运行时必须排除导入资料自动展开出的非 First Edition Machamp。

可信度说明：

- `HistoricallyVerified` 在这里表示“卡槽数量、Holo 平均比例和 Machamp 排除均有明确档案支持”，不表示复刻工厂印刷表或整盒固定位置。
- 论文明确指出 Energy、Uncommon、Rare/Holo 的精确工厂序列仍未知，因此当前实现只在每个已验证卡槽池内等权抽取。
- 1/3 Holo 是 153 包样本的平均值，不实现固定每三包必出，也不实现整盒 36 包的确定位置。
- Shadowless、First Edition 与其他语言需要独立 Profile 和独立证据，不能复用本 Profile 的 `HistoricallyVerified` 标记。

## Neo Genesis First Edition（英文）

规则 Profile：`pokemon-neo1-first-edition-psa-v1`

实现范围：

- 7 张 Common，其中包含该系列标记为 Common 的基础能量。
- 3 张 Uncommon。
- 1 张 Rare；Holo Rare 平均约三包一张。
- 同一个卡槽池内不产生重复 Printing。
- 只使用英文、带 `first-edition` Trait 的印刷版本。

来源：

1. PSA Set Registry 的 Neo Genesis First Edition 档案：<https://www.psacard.com/articles/articleview/9409/public/locales>
   - 记载英文 Neo Genesis First Edition 于 2000 年 12 月 16 日发行。
   - 每包 11 张：7 Common、3 Uncommon、1 Rare。
   - Holographic 卡约每三包出现一张。
   - 每盒 36 包，第一版包装带有 Edition 1 标记。

可信度说明：

- 本 Profile 验证的是第一版卡包的卡槽数量和 Holo 平均比例，不代表 Unlimited 版本，也不复刻工厂印刷表、整盒固定位置或 Wrapper 对内容的影响。
- 来源没有把 7 张 Common 进一步描述成“固定能量槽”；因此实现从包含基础能量的完整 Common 池抽取，不声称每包必定有能量。
- Holo 的 1/3 是近似平均比例，不实现固定每三包必出。
- 来源没有公布各张 Rare 的精确个体概率；当前用类别权重维持 Holo 总概率为 1/3，类别内部等权。
- 本机 TCGdex 导入资料为每张卡提供第一版 Trait，但图片不单独表现实体卡的 Edition 1 印章；收藏身份仍按 Printing Variant 分开计数。

## EX Ruby & Sapphire（国际英文版）

规则 Profile：`pokemon-ex1-psa-empirical-v1`

实现范围：

- 每包 9 张：5 Common、2 Uncommon、1 Reverse Holo、1 Rare。
- Rare 槽按整盒经验值分为 Non-Holo Rare 26.5/36、常规 Holo Rare 6.5/36、Pokémon-ex 3/36。
- 只使用英文、非 `first-edition`、非 `w-promo` 的印刷版本。
- 本机 manifest 的卡池边界为 40 Common、34 Uncommon、101 Reverse、13 Non-Holo Rare、16 常规 Holo Rare 和 8 Pokémon-ex。
- 同一个卡槽池内不产生重复 Printing；Reverse 与 Rare 是两个独立卡槽。

来源：

1. PSA Set Registry 的 EX Ruby & Sapphire 档案：<https://www.psacard.com/articles/articleview/9800/psa-set-registry-collecting-2003-poke-mon-ex-ruby-sapphire-first-nintendo-card-issue>
   - 记载每包一般包含 5 Common、2 Uncommon、1 Reverse Holo 和 1 Rare，共 9 张。
   - 作者开盒观察到每盒 36 包约有 6–7 张常规 Holo，并约有 3 张 Pokémon-ex。
   - 来源同时明确指出 Nintendo 没有公布 Pokémon-ex 的精确插入率。

可信度说明：

- 本 Profile 为 `HistoricallyVerified + Corroborated`：九张卡结构和经验整盒比例有可引用档案，但来源不是 Nintendo 官方配列表，因此不升级为 `Authoritative`。
- 6.5/36 和 3/36 是对来源“6–7 张”与“约 3 张”的可重复模拟换算，不表示每盒固定命中这些数量。
- 剩余 26.5/36 归入 Non-Holo Rare，使 Rare 槽类别权重合计为 36；类别内部仍按本机 manifest 中的 Printing 等权抽取。
- 当前实现不复刻工厂印刷序列、整盒固定位置、Wrapper 映射或 Pokémon-ex 的官方插入算法；获得更多一手资料后应建立新版本 Profile，而不是静默改写本 Profile。

## Sword & Shield Base（国际英文版）

规则 Profile：`pokemon-swsh1-sourced-simulation-v1`

实现范围：

- 模拟器每包记录 10 张可收藏系列卡：5 Common、3 Uncommon、1 Reverse Holo、1 Rare。
- 实体包的 Basic Energy 和 code card 不在 swsh1 系列 manifest 中，当前作为非收藏插入物明确省略，不用错误系列卡代替。
- Rare 槽权重为 Non-Holo Rare 59.52%、常规 Holo Rare 18.20%、Holo Rare V 14.20%、Holo Rare VMAX 2.20%、Ultra Rare 3.74%、Rainbow/Secret Rare 合计 2.14%。
- 本机卡池边界为 60 Common、56 Uncommon、164 Reverse、32 Non-Holo Rare、17 常规 Holo Rare、17 Holo Rare V、4 Holo Rare VMAX、16 Ultra Rare 和 14 Rainbow/Secret Rare。
- 只使用英文、非 `first-edition`、非 `w-promo` 的印刷版本。

来源：

1. Pokémon Support 的 booster 说明：<https://support.pokemon.com/hc/en-us/articles/360000981613-What-can-I-expect-in-a-Pok%C3%A9mon-Trading-Card-Game-booster-pack>
   - 官方明确说明 Scarlet & Violet 之前的系列每包至少保证 1 张 Reverse foil。
   - 官方不保证某个具体角色或高稀有度类型会在指定卡包出现。
2. Elite Fourum 的英文开包汇总：<https://www.elitefourum.com/t/pull-rates-in-sun-moon-sword-shield-sets/25220>
   - Sword & Shield Base 样本为 4,628 包，数据来自英文开包视频汇总。
   - 样本记录 Ultra Rare 3.74%、Holo Rare V 14.20%、Holo Rare VMAX 2.20%、Rainbow Rare 1.23%、Secret Rare 0.91%。
3. CardCodex 的 Sword & Shield Base 资料页：<https://cardcodex.com/pokemon/sword-shield/sword-shield-base/>
   - 资料页列出 5 Common、3 Uncommon、1 Energy，以及 Reverse 与 Rare 类别的逐包估算。
   - 卡表将 #34 与 #35 Cinderace 列为 Holo Rare，可用于校正当前 TCGdex manifest 把它们误标成 Holo Rare VMAX 的资料异常。

可信度说明：

- 本 Profile 为 `SourceInformedSimulation + Corroborated`，不是 `HistoricallyVerified`：官方资料只覆盖 Reverse 保证等边界，高稀有度权重来自开包样本和第三方资料。
- Non-Holo Rare 59.52% 是从 100% 扣除其余已列 Rare 类别后的剩余权重；CardCodex 的常规 Holo 18.20% 与 Elite Fourum 的高稀有度样本共同构成模拟，不代表官方插入率。
- TCGdex 将 Rainbow 与 Gold Secret 都映射为 `Secret Rare`，因此当前把 1.23% 与 0.91% 合并成 2.14%，再在 14 张本机 Printing 内等权抽取。
- 当前不实现整盒固定命中数、印刷批次、code card 颜色映射或同稀有度卡牌的工厂权重；取得一手资料前不得升级为 `HistoricallyVerified`。

## Scarlet & Violet Base（国际英文版）

规则 Profile：`pokemon-sv01-sourced-simulation-v1`

实现范围：

- 模拟器每包记录 10 张可收藏系列卡：4 Common、3 Uncommon、1 个第一闪卡位、1 个第二闪卡位和 1 个 Rare-or-higher 槽。
- 实体包额外包含的 Basic Energy 和 code card 不在 sv01 收藏 manifest 中，当前作为非收藏插入物明确省略。
- 第一闪卡位从 Common、Uncommon、Rare 的标准 Reverse Printing 中抽取。
- 第二闪卡位权重为标准 Reverse 87.33%、Illustration Rare 7.67%、Special Illustration Rare 3.15%、Hyper Rare 1.85%。
- Rare-or-higher 槽权重为常规 Holo Rare 79.67%、Double Rare 13.76%、Ultra Rare 6.57%。
- `PokemonImportedCardVariantPolicy` 只在运行时补正 sv01 的实体闪卡形态，不改写私人原始 manifest；本系列因此形成 444 个可抽取 Printing：105 Common normal、60 Uncommon normal、186 个标准 Reverse、21 Holo Rare、12 Double Rare、20 Ultra Rare、24 Illustration Rare、10 Special Illustration Rare 和 6 Hyper Rare。

来源：

1. Pokémon Support 的 booster 说明：<https://support.pokemon.com/hc/en-us/articles/360000981613-What-can-I-expect-in-a-Pok%C3%A9mon-Trading-Card-Game-booster-pack>
   - 官方说明 Scarlet & Violet 系列每包包含 4 Common、3 Uncommon 和 3 张 foil，其中至少 1 张为 Rare 或更高稀有度；另有 1 张 Basic Energy 与 1 张 code card。
   - 官方没有公开各高稀有度在三个闪卡位中的精确插入率。
2. TCGplayer 的 Scarlet & Violet 开包研究：<https://www.tcgplayer.com/content/article/Pok%C3%A9mon-TCG-Scarlet-Violet-Pull-Rates/a7702fce-dd64-4a58-beb1-0f871c853215/>
   - 样本超过 8,000 包，记录 Double Rare 13.76%、Ultra Rare 6.57%、Illustration Rare 7.67%、Special Illustration Rare 3.15%、Hyper Rare 1.85%。
   - 研究把 Illustration Rare、Special Illustration Rare 与 Hyper Rare 归入第二个 Reverse/foil 位置，把 Double Rare 与 Ultra Rare 归入 Rare 位置；当前权重按这两个位置分别归一。

可信度说明：

- 本 Profile 为 `SourceInformedSimulation + Corroborated`，不是 `HistoricallyVerified`：槽位数量有官方边界，高稀有度概率来自大样本开包研究。
- 标准 Reverse 87.33% 与常规 Holo Rare 79.67% 分别是对应卡位扣除已列高稀有度概率后的剩余权重，不是 Pokémon 官方公开的插入率。
- 同一类别内的 Printing 当前等权抽取；不实现工厂印刷序列、批次差异、整盒固定命中、同稀有度个体权重或 foil 图案差异。
- 若未来取得更权威或分地区资料，应新建版本化 Profile；不得静默把本模拟升级成历史已验证规则。
