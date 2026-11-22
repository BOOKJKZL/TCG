import Link from "next/link";
import { requireChatGPTUser, chatGPTSignOutPath } from "@/app/chatgpt-auth";
import { getContentBucket } from "@/lib/content/bindings";
import { readPublishedStatus } from "@/lib/content/content-store";
import { getOwnerAccess } from "@/lib/content/owner-access";
import { getPublisherCredentialStatus } from "@/lib/content/publisher-credential";
import { readLatestAndroidRelease } from "@/lib/releases/android-release-store";
import ReleasePublisher from "./release-publisher";

export const dynamic = "force-dynamic";

export default async function AdminPage() {
  const user = await requireChatGPTUser("/admin");
  const access = getOwnerAccess(user.email);
  if (!access.allowed) {
    return <AccessDenied message={access.message} />;
  }

  let status: Awaited<ReturnType<typeof readPublishedStatus>> | undefined;
  let credential: Awaited<ReturnType<typeof getPublisherCredentialStatus>> | undefined;
  let androidRelease: Awaited<ReturnType<typeof readLatestAndroidRelease>> | undefined;
  try {
    const bucket = getContentBucket();
    const [statusResult, credentialResult, androidReleaseResult] = await Promise.allSettled([
      readPublishedStatus(bucket),
      getPublisherCredentialStatus(bucket),
      readLatestAndroidRelease(bucket),
    ]);
    if (statusResult.status === "fulfilled") status = statusResult.value;
    if (credentialResult.status === "fulfilled") credential = credentialResult.value;
    if (androidReleaseResult.status === "fulfilled") androidRelease = androidReleaseResult.value;
  } catch {
    // An unavailable bucket leaves each status unknown until the next page load.
  }

  return (
    <main className="admin-shell">
      <nav className="admin-nav" aria-label="发布台导航">
        <Link href="/">← 返回中继站</Link>
        <a href={chatGPTSignOutPath("/")}>退出发布账号</a>
      </nav>
      <p className="admin-kicker">OWNER RELEASE CONSOLE</p>
      <h1 className="admin-title">内容与 APK 发布台</h1>
      <p className="admin-intro">
        这里只负责授权电脑端私人发布器，不读取浏览器中的本机文件。电脑会核对内容包和 APK 的
        字节数与 SHA-256，全部进入 R2 并完成公开读回验证后才切换最新版。
      </p>
      <ReleasePublisher
        initialStatus={status}
        initialCredential={credential}
        initialAndroidRelease={androidRelease}
      />
    </main>
  );
}

function AccessDenied({ message }: { message: string }) {
  return (
    <main className="access-denied">
      <p className="admin-kicker">ACCESS STOPPED</p>
      <h1>{message}</h1>
      <p><Link href="/">返回内容中继站</Link></p>
    </main>
  );
}
