import Link from "next/link";
import { requireChatGPTUser, chatGPTSignOutPath } from "@/app/chatgpt-auth";
import { getContentBucket } from "@/lib/content/bindings";
import { readPublishedStatus } from "@/lib/content/content-store";
import { getOwnerAccess } from "@/lib/content/owner-access";
import ReleasePublisher from "./release-publisher";

export const dynamic = "force-dynamic";

export default async function AdminPage() {
  const user = await requireChatGPTUser("/admin");
  const access = getOwnerAccess(user.email);
  if (!access.allowed) {
    return <AccessDenied message={access.message} />;
  }

  let status: { published: boolean; revision?: number; packageCount?: number };
  try {
    status = await readPublishedStatus(getContentBucket());
  } catch {
    status = { published: false };
  }

  return (
    <main className="admin-shell">
      <nav className="admin-nav" aria-label="发布台导航">
        <Link href="/">← 返回中继站</Link>
        <a href={chatGPTSignOutPath("/")}>退出发布账号</a>
      </nav>
      <p className="admin-kicker">OWNER RELEASE CONSOLE</p>
      <h1 className="admin-title">不可变内容发布台</h1>
      <p className="admin-intro">
        选择本机生成的 catalog 与 ZIP。系统会逐包核对字节数和 SHA-256，全部进入 R2 后才发布 catalog。
        发布途中失败不会破坏手机当前可见的版本。
      </p>
      <ReleasePublisher initialStatus={status} />
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
