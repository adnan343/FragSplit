"use client";
import { useState } from "react";
import {
  Bar,
  BarChart,
  CartesianGrid,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import { Feedback, useResource } from "@/components/Admin";
import { money } from "@/lib/shop";
type Bucket = { name: string; sales: number; units: number };
type Report = {
  gross: number;
  refunds: number;
  netSales: number;
  paidOrders: number;
  orders: number;
  cod: number;
  pending: number;
  units: number;
  lowStock: number;
  online: number;
  awaitingCod: number;
  cancelled: number;
  days: { date: string; revenue: number; orders: number }[];
  products: Bucket[];
  categories: Bucket[];
  sizes: Bucket[];
};
function date(days: number) {
  return new Intl.DateTimeFormat("en-CA", {
    timeZone: "Asia/Dhaka",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).format(new Date(Date.now() - days * 86400000));
}
export default function Dashboard() {
  const [range, set] = useState({ from: date(29), to: date(0) });
  const { data: r, error } = useResource<Report>(
    "/admin/analytics/overview?" + new URLSearchParams(range),
  );
  return (
    <>
      <h1>Sales overview</h1>
      <div className="filters">
        {[
          ["Today", 1],
          ["Last 7 days", 7],
          ["Last 30 days", 30],
        ].map(([label, n]) => (
          <button
            key={label}
            onClick={() => set({ from: date(Number(n) - 1), to: date(0) })}
          >
            {label}
          </button>
        ))}
        <label>
          From{" "}
          <input
            type="date"
            value={range.from}
            onChange={(e) => set({ ...range, from: e.target.value })}
          />
        </label>
        <label>
          To{" "}
          <input
            type="date"
            value={range.to}
            onChange={(e) => set({ ...range, to: e.target.value })}
          />
        </label>
      </div>
      <Feedback error={error} />
      {r && (
        <>
          <div className="kpis">
            {[
              ["Gross receipts", money(r.gross)],
              ["Net sales", money(r.netSales)],
              ["Refunds", money(r.refunds)],
              ["Paid online", money(r.online)],
              ["COD awaiting collection", money(r.awaitingCod)],
              ["Paid orders", r.paidOrders],
              ["COD orders", r.cod],
              ["Pending orders", r.pending],
              ["Cancelled orders", r.cancelled],
              ["Decants sold", r.units],
              ["Low-stock perfumes", r.lowStock],
            ].map(([k, v]) => (
              <div className="panel" key={k}>
                <span>{k}</span>
                <strong>{v}</strong>
              </div>
            ))}
          </div>
          <section className="section">
            <h2>Sales and orders</h2>
            <p>Daily totals in Asia/Dhaka</p>
            <ResponsiveContainer width="100%" height={260}>
              <BarChart data={r.days}>
                <CartesianGrid stroke="var(--line)" strokeDasharray="3 3" />
                <XAxis
                  stroke="var(--muted)"
                  dataKey="date"
                  tickFormatter={(v) => v.slice(5)}
                />
                <YAxis stroke="var(--muted)" yAxisId="bdt" />
                <YAxis
                  stroke="var(--muted)"
                  yAxisId="orders"
                  orientation="right"
                  allowDecimals={false}
                />
                <Tooltip
                  cursor={{ fill: "var(--art-ground)" }}
                  contentStyle={{
                    backgroundColor: "var(--field)",
                    borderColor: "var(--field-border)",
                    color: "var(--ink)",
                  }}
                />
                <Bar
                  yAxisId="bdt"
                  name="Net sales (BDT)"
                  dataKey="revenue"
                  fill="var(--accent)"
                />
                <Bar
                  yAxisId="orders"
                  name="Orders"
                  dataKey="orders"
                  fill="var(--chart-orders)"
                />
              </BarChart>
            </ResponsiveContainer>
            <details>
              <summary>Daily figures</summary>
              {r.days.map((d) => (
                <p key={d.date}>
                  {d.date} · {money(d.revenue)} · {d.orders} orders
                </p>
              ))}
            </details>
          </section>
          {(
            [
              [r.products, "Best-selling fragrances"],
              [r.categories, "Sales by category"],
              [r.sizes, "Sales by size"],
            ] as const
          ).map(([rows, title]) => (
            <section className="section" key={title}>
              <h2>{title}</h2>
              {rows.length ? (
                <table>
                  <thead>
                    <tr>
                      <th>Group</th>
                      <th>Net decants</th>
                      <th>Merchandise sales</th>
                    </tr>
                  </thead>
                  <tbody>
                    {rows.map((b, i) => (
                      <tr key={i}>
                        <td>{b.name}</td>
                        <td>{b.units}</td>
                        <td>{money(b.sales)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              ) : (
                <p>No sales in this period.</p>
              )}
            </section>
          ))}
        </>
      )}
    </>
  );
}
