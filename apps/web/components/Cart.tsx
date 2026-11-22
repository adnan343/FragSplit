"use client";
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useRef,
  useState,
} from "react";
import Link from "next/link";
import { api, message, money } from "@/lib/shop";
export type CartLine = {
  perfumeId: number;
  sizeMl: number;
  quantity: number;
  name: string;
  slug: string;
  price: number;
};
type Session = { id: number | null; email: string | null; role: string | null };
type SavedBag = { accountId: number; cartVersion: string; lines: CartLine[] };
type BagState = { session: Session; version: string | null; lines: CartLine[] };
type CartValue = {
  lines: CartLine[];
  count: number;
  ready: boolean;
  session: Session;
  version: string | null;
  add: (line: CartLine) => void;
  set: (id: number, size: number, n: number) => void;
  clear: () => void;
  show: () => void;
  refresh: () => Promise<void>;
};
const guestKey = "fragsplit.cart.v1";
const guest: Session = { id: null, email: null, role: null };
const CartContext = createContext<CartValue | null>(null);
export function useCart() {
  const c = useContext(CartContext);
  if (!c) throw new Error("Missing cart provider");
  return c;
}
function guestLines(): CartLine[] {
  try {
    const entries = JSON.parse(localStorage.getItem(guestKey) || "[]");
    return Array.isArray(entries)
      ? entries.filter(
          (l) =>
            l &&
            Number.isInteger(l.perfumeId) &&
            [3, 5, 10, 15, 30].includes(l.sizeMl) &&
            Number.isInteger(l.quantity) &&
            l.quantity > 0 &&
            l.quantity <= 99 &&
            typeof l.name === "string" &&
            typeof l.slug === "string" &&
            Number.isFinite(l.price),
        )
      : [];
  } catch {
    return [];
  }
}
function items(lines: CartLine[]) {
  return lines.map(({ perfumeId, sizeMl, quantity }) => ({
    perfumeId,
    sizeMl,
    quantity,
  }));
}
export function CartProvider({ children }: { children: React.ReactNode }) {
  const [bag, setBag] = useState<BagState>({
      session: guest,
      version: null,
      lines: [],
    }),
    [ready, setReady] = useState(false),
    [error, fail] = useState("");
  const drawer = useRef<HTMLDialogElement>(null),
    current = useRef(bag),
    queue = useRef(Promise.resolve());
  const publish = useCallback((value: BagState) => {
    current.current = value;
    setBag(value);
  }, []);
  const load = useCallback(async () => {
    const session = await api<Session>("/auth/session");
    const local = guestLines();
    if (session.id === null) {
      publish({ session, version: null, lines: local });
      return;
    }
    let remote = await api<SavedBag>("/account/cart");
    if (remote.accountId !== session.id)
      throw new Error("Your account changed. Refresh your bag.");
    if (local.length) {
      remote = await api<SavedBag>("/account/cart", {
        method: "PUT",
        body: JSON.stringify({
          accountId: session.id,
          version: remote.cartVersion,
          items: items(local),
          merge: true,
        }),
      });
      localStorage.removeItem(guestKey);
    }
    publish({ session, version: remote.cartVersion, lines: remote.lines });
  }, [publish]);
  const run = useCallback(
    (work: () => Promise<void>) => {
      queue.current = queue.current.then(async () => {
        setReady(false);
        try {
          await work();
          fail("");
          setReady(true);
        } catch (e) {
          try {
            await load();
            setReady(true);
          } catch {
            publish({ session: guest, version: null, lines: [] });
            setReady(false);
          }
          fail(message(e));
        }
      });
      return queue.current;
    },
    [load, publish],
  );
  const refresh = useCallback(() => run(load), [run, load]);
  useEffect(() => {
    void refresh();
    const focus = () => {
      void refresh();
    };
    window.addEventListener("focus", focus);
    window.addEventListener("storage", focus);
    return () => {
      window.removeEventListener("focus", focus);
      window.removeEventListener("storage", focus);
    };
  }, [refresh]);
  function change(transform: (lines: CartLine[]) => CartLine[], open = false) {
    void run(async () => {
      const { session, version, lines } = current.current;
      const next = transform(lines);
      if (session.id === null) {
        localStorage.setItem(guestKey, JSON.stringify(next));
        publish({ session, version: null, lines: next });
      } else {
        const remote = await api<SavedBag>("/account/cart", {
          method: "PUT",
          body: JSON.stringify({
            accountId: session.id,
            version,
            items: items(next),
          }),
        });
        publish({ session, version: remote.cartVersion, lines: remote.lines });
      }
      if (open && !drawer.current?.open) drawer.current?.showModal();
    });
  }
  function add(line: CartLine) {
    change((lines) => {
      const existing = lines.find(
        (l) => l.perfumeId === line.perfumeId && l.sizeMl === line.sizeMl,
      );
      return existing
        ? lines.map((l) =>
            l === existing
              ? { ...line, quantity: Math.min(99, l.quantity + line.quantity) }
              : l,
          )
        : [...lines, line];
    }, true);
  }
  function set(id: number, size: number, n: number) {
    change((lines) =>
      lines
        .map((l) =>
          l.perfumeId === id && l.sizeMl === size
            ? { ...l, quantity: Math.min(99, n) }
            : l,
        )
        .filter((l) => l.quantity > 0),
    );
  }
  const clear = useCallback(() => {
    if (current.current.session.id === null) localStorage.removeItem(guestKey);
    void refresh();
  }, [refresh]);
  const { lines, session, version } = bag;
  return (
    <CartContext.Provider
      value={{
        lines,
        count: lines.reduce((n, l) => n + l.quantity, 0),
        ready,
        session,
        version,
        add,
        set,
        clear,
        refresh,
        show: () => drawer.current?.showModal(),
      }}
    >
      {error && (
        <div className="wrap error" role="alert">
          {error} <button onClick={() => void refresh()}>Refresh bag</button>
        </div>
      )}
      {children}
      <dialog ref={drawer} className="drawer" aria-labelledby="bag-title">
        <div className="section-head">
          <h2 id="bag-title">Your bag</h2>
          <button onClick={() => drawer.current?.close()}>Close</button>
        </div>
        <CartList />
        {lines.length > 0 && (
          <Link
            className="button primary"
            href="/cart"
            onClick={() => drawer.current?.close()}
          >
            Review bag
          </Link>
        )}
      </dialog>
    </CartContext.Provider>
  );
}
export function CartList() {
  const c = useCart();
  return !c.lines.length ? (
    <p>Your bag is empty.</p>
  ) : (
    <>
      <div className="stack">
        {c.lines.map((l) => (
          <div className="line" key={l.perfumeId + "-" + l.sizeMl}>
            <div>
              <strong>{l.name}</strong>
              <p>
                {l.sizeMl} ml · {money(l.price)}
              </p>
            </div>
            <label>
              Quantity{" "}
              <input
                type="number"
                min={1}
                disabled={!c.ready}
                max={99}
                value={l.quantity}
                onChange={(e) => {
                  if (+e.target.value > 0)
                    c.set(l.perfumeId, l.sizeMl, Math.floor(+e.target.value));
                }}
              />
            </label>
            <button onClick={() => c.set(l.perfumeId, l.sizeMl, 0)}>
              Remove
            </button>
          </div>
        ))}
      </div>
      <p className="total">
        Subtotal{" "}
        <b>{money(c.lines.reduce((n, l) => n + l.price * l.quantity, 0))}</b>
      </p>
    </>
  );
}
