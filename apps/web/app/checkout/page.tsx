import { api, Settings } from "@/lib/shop";
import { CheckoutForm } from "@/components/CheckoutForm";
export const metadata = { title: "Checkout", robots: { index: false } };
export default async function Page() {
  return (
    <main id="main" className="wrap section">
      <h1>Checkout</h1>
      <CheckoutForm settings={await api<Settings>("/settings")} />
    </main>
  );
}
