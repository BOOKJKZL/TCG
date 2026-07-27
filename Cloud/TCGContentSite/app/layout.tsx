import type { Metadata } from "next";
import { Geist, Geist_Mono } from "next/font/google";
import "./globals.css";

const geistSans = Geist({
  variable: "--font-geist-sans",
  subsets: ["latin"],
});

const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
});

export const metadata: Metadata = {
  title: "Universal Gacha Content Relay",
  description:
    "Private-first release storage and mobile download gateway for Universal Gacha Simulator.",
  openGraph: {
    title: "Universal Gacha Content Relay",
    description: "Immutable card packs, verified before every mobile install.",
    type: "website",
    images: [{ url: "/og.png", alt: "Universal Gacha Content Relay" }],
  },
  twitter: {
    card: "summary_large_image",
    title: "Universal Gacha Content Relay",
    description: "Immutable card packs, verified before every mobile install.",
    images: ["/og.png"],
  },
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="zh-Hans">
      <body className={`${geistSans.variable} ${geistMono.variable}`}>
        {children}
      </body>
    </html>
  );
}
