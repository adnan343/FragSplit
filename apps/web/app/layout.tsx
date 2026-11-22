import "@fontsource-variable/geist";
import "@fontsource/instrument-serif/latin-400.css";
import "@fontsource/instrument-serif/latin-400-italic.css";
import "./globals.css";
import Link from "next/link";
import { cookies } from "next/headers";
import { parseTheme, themeCookie } from "@/lib/theme";
import { api, Settings } from "@/lib/shop";
import { CartProvider } from "@/components/Cart";
import { Header } from "@/components/Header";
export const dynamic = "force-dynamic";
export async function generateMetadata() {
  const { store } = await api<Settings>("/settings");
  return {
    metadataBase: new URL(process.env.PUBLIC_ORIGIN || "http://localhost:3000"),
    title: { default: store.name, template: "%s | " + store.name },
    description: "Explore fragrance decants in Bangladesh.",
  };
}
export default async function Layout({
  children,
}: {
  children: React.ReactNode;
}) {
  const { store } = await api<Settings>("/settings");
  const theme = parseTheme((await cookies()).get(themeCookie)?.value);
  return (
    <html lang="en" data-theme={theme}>
      <body>
        <a className="skip" href="#main">
          Skip to content
        </a>
        <CartProvider>
          <Header name={store.name} logo={store.logo} theme={theme} />
          {children}
        </CartProvider>
        <footer>
          <div className="wrap footer">
            <strong>{store.name}</strong>
            <Link href="/guide">Delivery & returns</Link>
            <Link href="/orders">Find an order</Link>
            <Link href="/admin">Store administration</Link>
            <span>Bangladesh · BDT ৳</span>
          </div>
        </footer>
      </body>
    </html>
  );
}
