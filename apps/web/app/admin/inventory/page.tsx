"use client";
import { useState } from "react";
import { Feedback, useResource } from "@/components/Admin";
import { api, message } from "@/lib/shop";
type Stock = {
  id: number;
  name: string;
  onHandMl: number;
  reservedMl: number;
  availableMl: number;
  threshold: number;
};
type Movement = {
  id: number;
  at: string;
  type: string;
  hand: number;
  reserved: number;
  actor: string;
  reason: string;
};
export default function Inventory() {
  const { data, error, reload } = useResource<Stock[]>("/admin/inventory"),
    [id, select] = useState(0),
    [size, setSize] = useState(5);
  return (
    <>
      <h1>Liquid inventory</h1>
      <label>
        Estimate decants at{" "}
        <select value={size} onChange={(e) => setSize(+e.target.value)}>
          {[3, 5, 10, 15, 30].map((n) => (
            <option value={n} key={n}>
              {n} ml
            </option>
          ))}
        </select>
      </label>
      <Feedback error={error} />
      <div className="table-scroll">
        <table>
          <thead>
            <tr>
              {[
                "Fragrance",
                "On hand",
                "Reserved",
                "Available",
                "Decants",
                "Action",
              ].map((h) => (
                <th key={h}>{h}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {data?.map((s) => (
              <tr key={s.id}>
                <td>
                  {s.name}
                  {s.availableMl <= s.threshold && (
                    <small>
                      {" "}
                      · {s.availableMl ? "Low stock" : "Out of stock"}
                    </small>
                  )}
                </td>
                <td>{s.onHandMl} ml</td>
                <td>{s.reservedMl} ml</td>
                <td>{s.availableMl} ml</td>
                <td>{Math.floor(s.availableMl / size)}</td>
                <td>
                  <button onClick={() => select(s.id)}>Manage</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {id > 0 && (
        <Adjust
          key={id}
          stock={data!.find((p) => p.id === id)!}
          refresh={reload}
        />
      )}
    </>
  );
}
function Adjust({ stock, refresh }: { stock: Stock; refresh: () => void }) {
  const { data, reload, error } = useResource<Movement[]>(
      "/admin/inventory/" + stock.id + "/movements",
    ),
    [err, fail] = useState(""),
    [key, setKey] = useState(() => crypto.randomUUID());
  return (
    <section className="panel narrow">
      <h2>{stock.name}</h2>
      <form
        className="form"
        onSubmit={async (e) => {
          e.preventDefault();
          const form = e.currentTarget,
            fd = new FormData(form);
          try {
            await api("/admin/inventory/" + stock.id + "/movements", {
              method: "POST",
              body: JSON.stringify({
                type: fd.get("type"),
                delta: Number(fd.get("delta")),
                reason: fd.get("reason"),
                key,
              }),
            });
            fail("");
            form.reset();
            setKey(crypto.randomUUID());
            refresh();
            reload();
          } catch (e) {
            fail(message(e));
          }
        }}
      >
        <label>
          Movement
          <select name="type">
            {["Restock", "Correction", "Spillage"].map((t) => (
              <option key={t}>{t}</option>
            ))}
          </select>
        </label>
        <label>
          Signed change in ml
          <input type="number" name="delta" required />
        </label>
        <label>
          Reason
          <input name="reason" required />
        </label>
        <button>Record adjustment</button>
      </form>
      <Feedback error={err || error} />
      <h3>Stock history</h3>
      {data?.map((m) => (
        <p key={m.id}>
          {new Date(m.at).toLocaleString("en-BD", { timeZone: "Asia/Dhaka" })} ·{" "}
          {m.type} · on hand {m.hand} ml · reserved {m.reserved} ml · {m.actor}{" "}
          · {m.reason}
        </p>
      ))}
    </section>
  );
}
