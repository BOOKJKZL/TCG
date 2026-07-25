# 卡包规则来源与可信度

最后更新：2026-07-25

本文件记录每一份 `HistoricallyVerified` 规则的证据、实现边界和不能推断的内容。没有列在这里的产品必须继续标记为 `Simulated`。

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
