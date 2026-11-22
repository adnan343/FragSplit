import { api, Settings } from "@/lib/shop";
import { BagPage } from "@/components/BagPage";
export const metadata = { title: "Your bag", robots: { index: false } };
export default async function Cart() {
  const { zones } = await api<Settings>("/settings");
  return (
    <main id="main" className="wrap narrow section">
      <h1>Your bag</h1>
      <BagPage rates={zones.map((z) => z.charge)} />
    </main>
  );
}
