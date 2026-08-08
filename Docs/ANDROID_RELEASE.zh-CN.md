# Android 正式版构建

正式 APK 与 smoke/emulator 产物完全分离。正式构建只允许通过 `Tools/Android/build_release_apk.ps1`，该入口会先执行 Unity Clean ARM64 签名构建，再执行独立 APK 静态审计。它不会上传 Site。

## 首次准备发行密钥

1. 在 Git 忽略的 `LocalContent/ReleaseSigning/` 下创建专用 keystore，或使用仓库外的私有路径。不得放入 `Assets/`、Git、APK、日志或公开配置。
2. 使用 JDK `keytool -genkeypair` 交互式创建密钥，不要把密码写进命令行、脚本或配置文件。
3. 至少制作两份加密离线备份，并记录保管位置。首次 stable 上传前必须由用户确认主副本和备份位置；丢失密钥会阻断覆盖升级。
4. 用 `apksigner verify --print-certs` 取得证书 SHA-256 指纹，将指纹保存在同一私有目录。每次正式审计必须显式传入预期指纹。

不要复用 Android debug keystore。Unity 在未启用 custom keystore 时会以调试密钥签名，该产物只能用于本地测试。

## 构建

在项目根目录执行：

```powershell
& Tools/Android/build_release_apk.ps1 `
  -VersionName 0.1.1 `
  -VersionCode 2 `
  -KeystorePath LocalContent/ReleaseSigning/universal-gacha-release.jks `
  -KeyAlias universal-gacha `
  -ExpectedCertificateSha256 <64位证书SHA-256>
```

脚本会安全提示输入 keystore 与 alias 密码；也可由 CI 临时注入 `TCG_ANDROID_KEYSTORE_PASSWORD` 和 `TCG_ANDROID_KEY_PASSWORD`。不要把这些变量永久写入用户或系统环境。

输出位于 `Builds/Android/Release/`：

- `UniversalGachaSimulator-release-<versionName>+<versionCode>.apk`
- 同名 `.release-audit.json`
- Unity 构建日志

构建入口会直接读取固定公网 `latest.json`，构建器在内存中设置版本、ARM64、APK 模式和 custom keystore，并在 `finally` 恢复原设置，因此不会要求把密码写入 `ProjectSettings.asset`。versionCode 必须严格高于公网当前 latest；无法读取或验证公网清单时正式构建关闭。

## 静态审计门槛

`Tools/Android/audit_release_apk.ps1` 必须全部通过：

- 包名、versionName、versionCode 与期望一致，versionCode 高于现有 stable。
- 只有 `arm64-v8a`，target SDK 至少 34，权限不越界。
- Manifest 未声明 `debuggable=true`。
- 无 Development/Profiler/Debugger/测试程序集标记。
- 签名有效、恰好一个 signer，且证书 SHA-256 与预期指纹完全一致。
- zipalign 通过，APK 内没有私有凭据、keystore 或本机内容路径。

审计通过只代表“本地正式候选”成立，不代表可发布。仍需完成干净安装、首次启动、Safe Area、内容下载和覆盖升级验收；P0-C Site stable gate 完成前也不得上传。
