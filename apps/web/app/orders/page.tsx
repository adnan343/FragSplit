"use client";
import Link from "next/link";
import { useRouter } from "next/navigation";
export default function Lookup() {
  const router = useRouter();
  return (
    <main id="main" className="wrap narrow section">
      <h1>Find your order</h1>
      <p>
        <Link href="/account">View my account orders</Link>
      </p>
      <form
        className="panel form"
        onSubmit={(e) => {
          e.preventDefault();
          const ref = String(
            new FormData(e.currentTarget).get("reference") || "",
          )
            .trim()
            .toUpperCase();
          router.push("/orders/" + encodeURIComponent(ref));
        }}
      >
        <p>
          Enter your reference on the device used at checkout, or open the
          private link you saved with your order.
        </p>
        <label>
          Order reference
          <input name="reference" required placeholder="FS-…" />
        </label>
        <button>Find order</button>
      </form>
    </main>
  );
}
