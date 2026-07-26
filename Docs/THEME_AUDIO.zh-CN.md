# 年代主题音效资产

最后更新：2026-07-27

## 定位与来源

阶段 8B 为五个年代主题各提供一个撕包音和一个稀有揭晓音，共十个正式烘焙 WAV。所有波形都由仓库内的确定性合成器原创生成，不含第三方录音、品牌提示音、角色声音或官方游戏音频。

运行时优先从 `AudioClipConfig` 读取这些正式资产；只有配置或资源缺失时，`AudioManager` 才使用原有的短促程序化后备音。统计与震动仍接收通用 `PackOpen` / `RareReveal` 语义事件，因此替换音频不会污染业务逻辑。

## 资产表

| 主题 | 撕包音 | 稀有揭晓音 | 设计方向 |
|---|---|---|---|
| vintage | `vintage-pack-open.wav`，0.56s | `vintage-rare-reveal.wav`，0.94s | 厚纸撕裂、低频收束、温暖旧式钟声 |
| forest | `forest-pack-open.wav`，0.58s | `forest-rare-reveal.wav`，0.98s | 柔和叶片摩擦、木质基音、向上生长的泛音 |
| ruby | `ruby-pack-open.wav`，0.48s | `ruby-rare-reveal.wav`，0.86s | 更紧的箔片撕裂、红色冲击感、明亮晶体和弦 |
| electric | `electric-pack-open.wav`，0.42s | `electric-rare-reveal.wav`，0.78s | 最快瞬态、细碎电弧、高频阶梯揭晓 |
| gallery | `gallery-pack-open.wav`，0.52s | `gallery-rare-reveal.wav`，1.02s | 轻盈珠光摩擦、玻璃泛音、最长的现代展示尾音 |

## 重建方式

编辑器菜单：

```text
Tools/Gacha/Generate Original Theme Audio
```

批处理：

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.0.73f1\Editor\Unity.exe" `
  -batchmode -nographics -projectPath . `
  -executeMethod Gacha.EditorTools.ThemeAudioAssetGenerator.GenerateBatch `
  -logFile TestResults/theme-audio-generate.log -quit
```

生成器会：

1. 用固定参数与固定种子生成单声道 16-bit PCM WAV。
2. 以 44.1 kHz、`DecompressOnLoad`、ADPCM、预加载方式导入，适合短促移动端音效。
3. 自动把十个稳定语义键写入 `Assets/Resources/Data/AudioClipConfig.asset`。
4. 保留配置中与年代主题无关的其他条目。

同一代码版本连续生成两次时，十个 WAV 的 SHA-256 必须全部不变。

## 自动验收

`ThemeAudioAssetTests` 与 `AudioManagerFeedbackTests` 会验证：

- 十个键、十个文件和五个主题成对完整。
- 实际导入声道、采样率、持续时间、加载方式、压缩格式和预加载设置。
- 解码后峰值、RMS、直流偏移和首尾淡化处于安全范围。
- 十个量化波形指纹均不同，不会误把同一个声音映射给多个主题。
- 开始场景使用填充后的 `AudioClipConfig`。
- 配置资产优先于程序化后备音。

## 将来替换规则

可以用人工录制或重新设计的原创 WAV 替换单个文件，但必须：

1. 保持对应语义键稳定，或通过生成器同步更新配置。
2. 明确记录素材来源与使用权限，不加入官方包装、角色或游戏音频。
3. 保持短音效、单声道和移动端合适的导入设置。
4. 重跑音质测试、完整 EditMode/PlayMode 和 Android 构建。

