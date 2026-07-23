# 卡包规则来源与可信度

最后更新：2026-07-23

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
