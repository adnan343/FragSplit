"use client";
import { FormEvent, useState } from "react";
import { api, Product, categories, message, Picture } from "@/lib/shop";
import { Feedback, useResource } from "@/components/Admin";
const sizes = [3, 5, 10, 15, 30];
export default function Products() {
  const { data, error, reload } = useResource<Product[]>("/admin/perfumes"),
    [id, choose] = useState<number | null>(null);
  const selected = data?.find((p) => p.id === id);
  return (
    <>
      <h1>Products</h1>
      <div className="columns">
        <div>
          <button onClick={() => choose(0)}>New fragrance</button>
          {data?.map((p) => (
            <div className="line" key={p.id}>
              <div>
                <b>{p.name}</b>
                <p>
                  {p.brand} · {p.active ? "Published" : "Archived"}
                </p>
              </div>
              <button onClick={() => choose(p.id)}>Edit {p.name}</button>
            </div>
          ))}
          <Feedback error={error} />
        </div>
        {(id === 0 || selected) && (
          <Editor
            key={id}
            p={selected}
            onSaved={(n) => {
              choose(n);
              reload();
            }}
          />
        )}
      </div>
    </>
  );
}
function Editor({
  p,
  onSaved,
}: {
  p?: Product;
  onSaved: (id: number) => void;
}) {
  const [error, fail] = useState(""),
    [text, ok] = useState(""),
    [saving, setSaving] = useState(false);
  async function save(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    const fd = new FormData(e.currentTarget);
    const body = {
      ...Object.fromEntries(fd),
      active: fd.has("active"),
      featured: fd.has("featured"),
      threshold: Number(fd.get("threshold")),
      ...(!p && { openingStockMl: Number(fd.get("openingStockMl")) }),
      variants: sizes.map((n) => ({
        sizeMl: n,
        price: Number(fd.get("price" + n)),
        enabled: fd.has("enabled" + n),
      })),
    };
    try {
      setSaving(true);
      const r = await api<{ id: number }>(
        p ? "/admin/perfumes/" + p.id : "/admin/perfumes",
        { method: p ? "PATCH" : "POST", body: JSON.stringify(body) },
      );
      onSaved(r.id);
      fail("");
      ok("Listing saved.");
    } catch (e) {
      fail(message(e));
    } finally {
      setSaving(false);
    }
  }
  return (
    <section className="panel">
      <h2>{p ? "Edit fragrance" : "New fragrance"}</h2>
      <form className="form" onSubmit={save}>
        {(
          [
            ["name", "Perfume name"],
            ["slug", "URL slug"],
            ["brand", "Brand"],
            ["description", "Description"],
            ["top", "Top notes"],
            ["heart", "Heart notes"],
            ["base", "Base notes"],
            ["concentration", "Concentration"],
            ["tags", "Search keywords"],
            ["seoTitle", "SEO title"],
            ["seoDescription", "SEO description"],
          ] as const
        ).map(([k, label]) => (
          <label key={k}>
            {label}
            <input
              name={k}
              defaultValue={p?.[k]}
              required={["name", "slug", "brand", "description"].includes(k)}
            />
          </label>
        ))}
        <label>
          Category
          <select name="category" defaultValue={p?.category}>
            {categories.map((c) => (
              <option key={c}>{c}</option>
            ))}
          </select>
        </label>
        {!p && (
          <div>
            <label htmlFor="opening-stock">Opening stock (ml)</label>
            <input
              id="opening-stock"
              type="number"
              name="openingStockMl"
              min={0}
              max={1000000}
              step={1}
              defaultValue={0}
              aria-describedby="opening-stock-help"
            />
            <small id="opening-stock-help">
              Total liquid available for all decant sizes.
            </small>
          </div>
        )}
        <label>
          Low stock threshold (ml)
          <input
            type="number"
            name="threshold"
            min={0}
            required
            defaultValue={p?.threshold ?? 30}
          />
        </label>
        {sizes.map((n) => {
          const v = p?.variants.find((v) => v.sizeMl === n);
          return (
            <div className="line" key={n}>
              <label>
                {n} ml price (BDT)
                <input
                  type="number"
                  min=".01"
                  step=".01"
                  name={"price" + n}
                  defaultValue={v?.price}
                  required
                />
              </label>
              <label>
                <span>
                  <input
                    type="checkbox"
                    name={"enabled" + n}
                    defaultChecked={v?.enabled ?? true}
                  />
                  Enabled
                </span>
              </label>
            </div>
          );
        })}
        <label>
          <span>
            <input
              type="checkbox"
              name="active"
              defaultChecked={p?.active ?? true}
            />
            Published
          </span>
        </label>
        <label>
          <span>
            <input
              type="checkbox"
              name="featured"
              defaultChecked={p?.featured}
            />
            Featured
          </span>
        </label>
        <button className="primary" disabled={saving}>
          {saving ? "Saving…" : "Save fragrance"}
        </button>
      </form>
      <Feedback error={error} text={text} />
      {p && (
        <GalleryEditor
          key={JSON.stringify(p.images)}
          p={p}
          refresh={() => onSaved(p.id)}
        />
      )}
    </section>
  );
}
function GalleryEditor({ p, refresh }: { p: Product; refresh: () => void }) {
  const [images, set] = useState<Picture[]>(p.images),
    [error, fail] = useState("");
  async function upload(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    const form = e.currentTarget;
    try {
      await api("/admin/perfumes/" + p.id + "/images", {
        method: "POST",
        body: new FormData(form),
      });
      form.reset();
      fail("");
      refresh();
    } catch (e) {
      fail(message(e));
    }
  }
  return (
    <>
      <h3>Product gallery</h3>
      <p>First image is primary.</p>
      {images.map((im, i) => (
        <div className="line" key={im.id}>
          <label>
            Image description
            <input
              value={im.alt}
              onChange={(e) =>
                set(
                  images.map((x) =>
                    x.id === im.id ? { ...x, alt: e.target.value } : x,
                  ),
                )
              }
            />
          </label>
          <button
            disabled={!i}
            onClick={() => {
              const a = images.slice();
              [a[i - 1], a[i]] = [a[i], a[i - 1]];
              set(a);
            }}
          >
            Move earlier
          </button>
          <button onClick={() => set(images.filter((x) => x.id !== im.id))}>
            Remove
          </button>
        </div>
      ))}
      <button
        onClick={async () => {
          try {
            await api("/admin/perfumes/" + p.id + "/images", {
              method: "PUT",
              body: JSON.stringify(images),
            });
            refresh();
            fail("");
          } catch (e) {
            fail(message(e));
          }
        }}
      >
        Save gallery order
      </button>
      <form className="form" onSubmit={upload}>
        <label>
          Image file
          <input
            type="file"
            name="image"
            accept="image/png,image/jpeg,image/webp"
            required
          />
        </label>
        <label>
          Image alt text
          <input name="alt" required />
        </label>
        <button>Upload image</button>
      </form>
      <Feedback error={error} />
    </>
  );
}
