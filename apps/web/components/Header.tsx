"use client";
import Link from "next/link";
import { Menu, Search, ShoppingBag, UserRound, X } from "lucide-react";
import { useState } from "react";
import { ThemePicker } from "./ThemePicker";
import type { Theme } from "@/lib/theme";
import { useCart } from "./Cart";

const navigation = [
  { href: "/fragrances", label: "Shop fragrances" },
  { href: "/#collections", label: "Collections" },
  { href: "/guide", label: "Decant guide" },
];

export function Header({
  name,
  logo,
  theme,
}: {
  name: string;
  logo: string;
  theme: Theme;
}) {
  const [open, setOpen] = useState(false);
  const cart = useCart();
  const links = navigation.map(({ href, label }) => (
    <Link key={href} href={href} onClick={() => setOpen(false)}>
      {label}
    </Link>
  ));
  const search = (
    <Link
      className="icon"
      href="/fragrances?focus=search"
      aria-label="Search fragrances"
      onClick={() => setOpen(false)}
    >
      <Search size={19} aria-hidden />
    </Link>
  );
  return (
    <header
      onKeyDown={(event) => {
        if (open && event.key === "Escape") {
          setOpen(false);
          event.currentTarget
            .querySelector<HTMLButtonElement>(
              "[aria-controls='mobile-navigation']",
            )
            ?.focus();
        }
      }}
    >
      <div className="wrap nav">
        <nav aria-label="Main">{links}</nav>
        <div className="mobile-tools">
          <button
            className="icon"
            aria-label="Toggle navigation"
            aria-expanded={open}
            aria-controls="mobile-navigation"
            onClick={() => setOpen(!open)}
          >
            {open ? (
              <X size={21} aria-hidden />
            ) : (
              <Menu size={21} aria-hidden />
            )}
          </button>
          {search}
        </div>
        <Link href="/" className="wordmark" onClick={() => setOpen(false)}>
          {logo && (
            <span
              className="logo-image"
              style={{ backgroundImage: "url(" + JSON.stringify(logo) + ")" }}
            />
          )}
          {name}
        </Link>
        <div className="actions">
          <span className="desktop-search">{search}</span>
          <ThemePicker initialTheme={theme} />
          <Link
            href="/account"
            className="icon"
            aria-label="My account"
            title="My account"
            onClick={() => setOpen(false)}
          >
            <UserRound size={19} aria-hidden />
          </Link>
          <button
            className="bag"
            aria-label={`Bag (${cart.count})`}
            onClick={() => {
              setOpen(false);
              cart.show();
            }}
          >
            <ShoppingBag size={19} aria-hidden /> <span>{cart.count}</span>
          </button>
        </div>
      </div>
      <nav
        id="mobile-navigation"
        aria-label="Mobile"
        className="mobile-menu"
        hidden={!open}
      >
        {links}
      </nav>
    </header>
  );
}
