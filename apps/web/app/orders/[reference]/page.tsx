import { GuestOrder } from "@/components/GuestOrder";
export const metadata = { title: "Your order", robots: { index: false } };
export default async function Page({
  params,
}: {
  params: Promise<{ reference: string }>;
}) {
  return (
    <main id="main" className="wrap section narrow">
      <h1>Order details</h1>
      <GuestOrder reference={(await params).reference} />
    </main>
  );
}
