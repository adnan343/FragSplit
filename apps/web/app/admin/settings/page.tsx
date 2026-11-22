"use client";
import { Settings, Zone, api, message } from "@/lib/shop";
import { Feedback, useResource } from "@/components/Admin";
import { useState } from "react";
export default function Page() {
  const { data, error, reload } = useResource<Settings>("/admin/settings");
  return (
    <>
      <h1>Store settings</h1>
      <Feedback error={error} />
      {data && (
        <SettingsForm data={data} key={JSON.stringify(data)} reload={reload} />
      )}
    </>
  );
}
function SettingsForm({
  data,
  reload,
}: {
  data: Settings;
  reload: () => void;
}) {
  const [zones, set] = useState(data.zones),
    [error, fail] = useState(""),
    [text, ok] = useState("");
  function patch(i: number, change: Partial<Zone>) {
    set(zones.map((z, n) => (n === i ? { ...z, ...change } : z)));
  }
  return (
    <form
      className="form panel narrow"
      onSubmit={async (e) => {
        e.preventDefault();
        try {
          await api("/admin/settings", {
            method: "PATCH",
            body: JSON.stringify({
              ...Object.fromEntries(new FormData(e.currentTarget)),
              zones,
            }),
          });
          fail("");
          ok("Settings saved.");
          reload();
        } catch (e) {
          fail(message(e));
        }
      }}
    >
      {(
        [
          ["name", "Store name"],
          ["logo", "Logo image path"],
          ["contact", "Support contact"],
          ["shippingPolicy", "Shipping policy"],
          ["returnPolicy", "Return policy"],
        ] as const
      ).map(([k, l]) => (
        <label key={k}>
          {l}
          <textarea
            name={k}
            rows={k.endsWith("Policy") ? 3 : 1}
            defaultValue={data.store[k]}
          />
        </label>
      ))}
      <h2>Delivery regions</h2>
      {zones.map((z, i) => (
        <fieldset className="panel" key={i}>
          <legend>Region {i + 1}</legend>
          <label>
            Region name
            <input
              value={z.name}
              required
              onChange={(e) => patch(i, { name: e.target.value })}
            />
          </label>
          <label>
            Charge in BDT
            <input
              type="number"
              step=".01"
              min="0"
              value={z.charge}
              onChange={(e) => patch(i, { charge: +e.target.value })}
            />
          </label>
          <label>
            <span>
              <input
                type="checkbox"
                checked={z.cod}
                onChange={(e) => patch(i, { cod: e.target.checked })}
              />
              Allow COD
            </span>
          </label>
          <label>
            <span>
              <input
                type="checkbox"
                checked={z.active}
                onChange={(e) => patch(i, { active: e.target.checked })}
              />
              Active
            </span>
          </label>
        </fieldset>
      ))}
      <button
        type="button"
        onClick={() =>
          set([
            ...zones,
            { id: 0, name: "", charge: 0, cod: false, active: true },
          ])
        }
      >
        Add region
      </button>
      <button className="primary">Save settings</button>
      <Feedback error={error} text={text} />
    </form>
  );
}
