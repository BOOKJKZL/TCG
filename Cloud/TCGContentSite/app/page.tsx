import type { Metadata } from "next";
import Link from "next/link";
import AndroidReleaseDownload from "./android-release-download";

export const metadata: Metadata = {
  title: "内容中继站 | Universal Gacha",
  description: "Universal Gacha Simulator 的可迁移内容发布边界。",
};

export default function Home() {
  return (
    <main className="site-shell">
      <section className="hero" aria-labelledby="site-title">
        <div className="eyebrow">
          <span className="status-dot" /> TEMPORARY SITE STORAGE
        </div>
        <p className="hero-index" aria-hidden="true">UG / 01</p>
        <h1 id="site-title">
          卡包离开 APK，<br /><span>协议留在游戏里。</span>
        </h1>
        <p className="hero-copy">
          Universal Gacha Content Relay 暂时使用 Sites R2 保存不可变卡包。
          手机只读取经过大小与 SHA-256 验证的 catalog；未来迁往 Cloudflare R2 时，抽卡逻辑无需改写。
        </p>
        <div className="hero-actions">
          <a className="primary-action" href="#android-download">下载最新 APK</a>
          <a className="secondary-action" href="/api/content/catalog.json">查看公开 Catalog</a>
          <Link className="secondary-action" href="/admin">管理电脑发布器</Link>
        </div>
      </section>

      <AndroidReleaseDownload />

      <section className="protocol-grid" aria-label="内容发布协议">
        <article>
          <span className="card-number">01</span>
          <h2>电脑发布</h2>
          <p>电脑端导入器直接上传内容寻址 ZIP，核对真实字节与 Hash，最后才切换 catalog。</p>
        </article>
        <article>
          <span className="card-number">02</span>
          <h2>手机下载</h2>
          <p>支持严格 HTTP Range、断点续传、离线缓存和原子安装。</p>
        </article>
        <article>
          <span className="card-number">03</span>
          <h2>随时迁移</h2>
          <p>存储只是适配器。更换 Cloudflare R2 后，package ID、Hash 与存档身份保持不变。</p>
        </article>
      </section>

      <footer className="site-footer">
        <span>UNIVERSAL GACHA SIMULATOR</span>
        <span>CATALOG V2 · APK · ZIP · SHA-256 · RANGE</span>
      </footer>
    </main>
  );
}
