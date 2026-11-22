import { AdminShell } from "@/components/Admin";
export const metadata = {
  title: "Store administration",
  robots: { index: false, follow: false },
};
export default function Admin({ children }: { children: React.ReactNode }) {
  return <AdminShell>{children}</AdminShell>;
}
