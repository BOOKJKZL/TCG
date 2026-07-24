# Universal UI Chinese fallback

`UniversalUiChineseSubset.ttf` is a modified, renamed subset of Noto Sans SC.
It contains only the non-Latin characters currently used by game UI code and
Unity Localization tables, keeping the mobile package small.

Rebuild the TTF after adding Chinese UI text:

```powershell
python -m pip install fonttools
python Tools/Fonts/build_ui_font_subset.py `
  --source-font "C:\Windows\Fonts\NotoSansSC-VF.ttf" `
  --output-font "Assets/Fonts/UniversalUiChineseSubset.ttf"
```

Then run `Tools > Gacha > Rebuild Universal UI Font Asset` inside Unity. The
font is distributed under the SIL Open Font License 1.1; see
`NotoSansSC-OFL.txt` in this directory.
