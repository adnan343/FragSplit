"use client";
import { useRouter } from "next/navigation";
import { useRef, useEffect, useState } from "react";
import { Search } from "lucide-react";
import { categories } from "@/lib/shop";
export function Filter({
  values,
  brands,
}: {
  values: Record<string, string>;
  brands: string[];
}) {
  const router = useRouter(),
    [draft, setDraft] = useState(values),
    current = useRef(values),
    requested = useRef(new Set<string>()),
    timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const serverQuery = new URLSearchParams(values).toString();
  useEffect(() => {
    if (
      requested.current.delete(serverQuery) ||
      new URLSearchParams(current.current).toString() === serverQuery
    )
      return;
    if (timer.current) clearTimeout(timer.current);
    const next = Object.fromEntries(new URLSearchParams(serverQuery));
    current.current = next;
    // Synchronize an external link navigation with the local filter draft.
    setDraft(next);
  }, [serverQuery]);
  useEffect(() => {
    function restore() {
      if (timer.current) clearTimeout(timer.current);
      const p = Object.fromEntries(new URLSearchParams(location.search));
      requested.current.clear();
      current.current = p;
      setDraft(p);
    }
    window.addEventListener("popstate", restore);
    return () => {
      if (timer.current) clearTimeout(timer.current);
      window.removeEventListener("popstate", restore);
    };
  }, []);
  function apply(key: string, value: string, debounce = false) {
    if (timer.current) clearTimeout(timer.current);
    const next = { ...current.current };
    delete next.focus;
    delete next.page;
    if (value) next[key] = value;
    else delete next[key];
    current.current = next;
    setDraft(next);
    const navigate = () => {
      requested.current.add(new URLSearchParams(next).toString());
      router.push("/fragrances?" + new URLSearchParams(next), {
        scroll: false,
      });
    };
    if (debounce) timer.current = setTimeout(navigate, 300);
    else navigate();
  }
  const selects = [
    { key: "category", label: "All collections", options: categories },
    { key: "brand", label: "All houses", options: brands },
    {
      key: "sort",
      label: "Newest",
      options: ["name", "price-asc", "price-desc", "best"],
    },
  ];
  const labels: Record<string, string> = {
    name: "Name A–Z",
    "price-asc": "Price · low to high",
    "price-desc": "Price · high to low",
    best: "Best selling",
  };
  return (
    <div className="filters">
      <label className="search">
        <Search size={19} />
        <input
          aria-label="Search by fragrance, brand or note"
          autoFocus={values.focus === "search"}
          placeholder="Search by fragrance, brand or note"
          value={draft.search || ""}
          onChange={(e) => apply("search", e.target.value, true)}
        />
      </label>
      {selects.map(({ key, label, options }) => (
        <label key={key}>
          <span className="sr-only">{key}</span>
          <select
            value={draft[key] || ""}
            onChange={(e) => apply(key, e.target.value)}
          >
            <option value="">{label}</option>
            {options.map((o) => (
              <option key={o} value={o}>
                {labels[o] || o}
              </option>
            ))}
          </select>
        </label>
      ))}
      <label>
        <input
          type="checkbox"
          checked={draft.available === "true"}
          onChange={(e) => apply("available", e.target.checked ? "true" : "")}
        />{" "}
        In stock
      </label>
    </div>
  );
}
