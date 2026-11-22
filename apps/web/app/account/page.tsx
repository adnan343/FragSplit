import { CustomerAccount } from "@/components/CustomerAccount";
export const metadata = {
  title: "My account",
  robots: { index: false, follow: false },
};
export default function AccountPage() {
  return (
    <main id="main" className="wrap narrow section">
      <h1>My account</h1>
      <CustomerAccount />
    </main>
  );
}
