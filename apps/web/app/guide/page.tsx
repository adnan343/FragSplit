import { api, Settings } from "@/lib/shop";
export const metadata = { title: "Decant guide" };
export default async function Guide() {
  const { store, zones } = await api<Settings>("/settings");
  return (
    <main id="main" className="wrap section prose">
      <h1>The decant guide</h1>
      <h2 id="sizes">Choosing a size</h2>
      <p>
        A decant is a measured portion of a fragrance supplied in a separate
        atomizer. Choose 3 or 5 ml to explore a scent, or 10, 15 or 30 ml for a
        longer wear test. Each listing shows its available sizes and prices.
      </p>
      <h2>Delivery in Bangladesh</h2>
      <p>{store.shippingPolicy}</p>
      <ul>
        {zones.map((z) => (
          <li key={z.id}>
            {z.name} · ৳{z.charge}
            {z.cod ? " · Cash on delivery available" : ""}
          </li>
        ))}
      </ul>
      <h2>Returns and support</h2>
      <p>{store.returnPolicy}</p>
      {store.contact && <p>{store.contact}</p>}
    </main>
  );
}
